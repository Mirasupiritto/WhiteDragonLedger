"""Read a compact allocation receipt from the current stock transaction."""
import json
from functools import lru_cache

from search_text import chinese_form, search_text


# Input aliases only. Item membership always comes from the game's GetGameItemType().
CATEGORY_NAMES = {"食品": "FOOD", "食物": "FOOD", "日用品": "HOUSEHOLD_GOOD", "材料": "MATERIAL", "水": "WATER"}
# Seek the next distinct category combination in the B-tree; identical stacks share one key.
CATEGORY_KEYS = """
WITH RECURSIVE keys(value) AS (
    SELECT MIN(categories) FROM stock
    UNION ALL
    SELECT (SELECT MIN(categories) FROM stock WHERE categories > keys.value)
    FROM keys WHERE value IS NOT NULL
)
SELECT value FROM keys WHERE value IS NOT NULL
"""


STOCK_DETAIL_COLUMNS = (
    "size_w,size_h,slot_x,slot_y,rotatable,"
    "water_ml,liquid_ml,capacity_ml,empty,water_container,contains_water,liquid_main,"
    "charge_remaining,charge_max,liters_remaining,estimated_value,parent_id,container_path,access"
)


def row_to_item(row, *, include_categories=False):
    raw = dict(row)
    item = {}
    for key in ("id", "name", "kind", "area", "owned", "quantity", "bottle_label", "machine_id", "can_take_now", "save_id"):
        if raw.get(key) is not None:
            item[key] = raw[key]
    if raw.get("liquid_main"):
        item["liquid_main"] = json.loads(raw["liquid_main"])
    if raw.get("parent_id") is not None:
        item.update(parent_id=raw["parent_id"], container_path=json.loads(raw["container_path"]), access=raw["access"])
    item["owned"] = bool(item["owned"])
    if "can_take_now" in item:
        item["can_take_now"] = bool(item["can_take_now"])
    if "save_id" in item and not item["save_id"]:
        del item["save_id"]
    if include_categories and raw.get("categories") is not None:
        item["categories"] = json.loads(raw["categories"])
    if raw.get("size_w") is not None and raw.get("size_h") is not None:
        item["size"] = {"w": raw["size_w"], "h": raw["size_h"]}
    if raw.get("slot_x") is not None and raw.get("slot_y") is not None:
        item["slot"] = {"x": raw["slot_x"], "y": raw["slot_y"]}
    if raw.get("rotatable") is not None:
        item["rotatable"] = bool(raw["rotatable"])
    for flag in ("empty", "water_container", "contains_water"):
        if raw.get(flag) is not None:
            item[flag] = bool(raw[flag])
    for metric in ("water_ml", "liquid_ml", "capacity_ml", "charge_remaining", "charge_max", "liters_remaining", "estimated_value"):
        if raw.get(metric) is not None:
            item[metric] = raw[metric]
    return item


@lru_cache(maxsize=1)
def native_categories(folder, generation):
    path = folder / f"{generation}-stock-categories.json"
    return json.loads(path.read_text(encoding="utf-8-sig"))


def category_vocabulary(folder, db, generation):
    labels = native_categories(folder, generation)
    combinations = {key: set(json.loads(key)) for (key,) in db.execute(CATEGORY_KEYS)}
    present = set().union(*combinations.values())
    codes = labels.keys() | present
    names = {}
    for code in codes:
        for term in (code, labels.get(code, code)):
            names.setdefault(search_text(term), set()).add(code)
    for alias, code in CATEGORY_NAMES.items():
        if code in codes:
            names.setdefault(search_text(alias), set()).add(code)
    return labels, combinations, present, names


def list_tags(db, receipt, query, labels, present, names):
    term = search_text((query.get("search") or "").strip())
    matches = set().union(*(codes for name, codes in names.items() if term in name))
    if query.get("in_stock_only"):
        matches &= present
    categories = [{"code": code, "label": labels.get(code, code), "in_stock": code in present}
                  for code in sorted(matches)]
    bottles = [label for (label,) in db.execute(
        "SELECT DISTINCT bottle_label FROM stock WHERE bottle_label IS NOT NULL ORDER BY bottle_label"
    ) if term in search_text(label)]
    return {**receipt, "categories": categories, "bottle_labels": bottles}


def select_stock(folder, db, head, query):
    receipt = {key: head[key] for key in ("status", "slot", "generation", "revision", "load_scans")}
    if "item_ids" in query and query["generation"] != head["generation"]:
        return {**receipt, "status": "stale_generation", "items": []}
    filters, params = [], []

    def within(column, values, negative=False):
        op = "NOT IN" if negative else "IN"
        params.append(json.dumps(list(values)))
        return f"{column} {op} (SELECT value FROM json_each(?))"

    if query.get("exclude_contraband") or query.get("area") == "purifier":
        if not head.get("trade_panel_protocol"):
            return {**receipt, "status": "reload_updated_mod", "items": []}
    if query.get("exclude_contraband"):
        filters.append("trade_contraband = 0")
    buy = query.get("categories") or []
    avoid = query.get("exclude_categories") or []
    include, exclude = set(), set()
    if buy or avoid or query.get("list_tags"):
        if not head.get("stock_categories_protocol"):
            return {**receipt, "status": "reload_updated_mod", "items": []}
        labels, combinations, present, names = category_vocabulary(folder, db, head["generation"])
        if query.get("list_tags"):
            return list_tags(db, receipt, query, labels, present, names)
        # Generic buyer rules can appear alongside ordinary category codes.
        unknown = []
        for requested, resolved in ((buy, include), (avoid, exclude)):
            for tag in requested:
                key = tag.strip()
                if key.upper() in ("ALL_TYPE", "ALL_TYPE_EXCEPT_CONTRABAND"):
                    resolved.add(key.upper())
                elif search_text(key) in names:
                    resolved.update(names[search_text(key)])
                else:
                    unknown.append(tag)
        if unknown:
            return {**receipt, "status": "unknown_categories", "unknown_categories": list(dict.fromkeys(unknown)),
                    "items": []}
    if "ALL_TYPE" in exclude:
        return {**receipt, "items": []}
    if "ALL_TYPE_EXCEPT_CONTRABAND" in exclude:
        exclude.remove("ALL_TYPE_EXCEPT_CONTRABAND")
        filters.append("contraband = 1")
    kinds = query.get("kinds")
    if "ALL_TYPE" in include:
        include.clear()
        kinds = None
    any_legal = "ALL_TYPE_EXCEPT_CONTRABAND" in include
    include.discard("ALL_TYPE_EXCEPT_CONTRABAND")
    choices = ["contraband = 0"] if any_legal else []
    if include or exclude:
        # Exclusions apply even when a specific kind is requested.
        allowed, matching = [], []
        for key, tags in combinations.items():
            if tags.isdisjoint(exclude):
                allowed.append(key)
                if tags & include:
                    matching.append(key)
        if exclude:
            filters.append(within("categories", allowed))
        if include:
            if "WATER" in include:
                choices.append("((water_container = 1 AND (empty = 1 OR contains_water = 1)) OR " + within("categories", matching) + ")")
            else:
                choices.append(within("categories", matching))
    if kinds:
        choices.append(within("kind", kinds))
    if choices:
        filters.append("(" + " OR ".join(choices) + ")")
    if query.get("exclude_kinds"):
        filters.append(within("kind", query["exclude_kinds"], negative=True))
    label = query.get("bottle_label")
    if label is not None:
        filters.append(within("bottle_label", {label, chinese_form(label), chinese_form(label, traditional=True)}))
    for field in ("area", "owned"):
        if query.get(field) is not None:
            filters.append(field + " = ?")
            params.append(query[field])
    ids = query.get("item_ids")
    if ids is not None:
        filters.append(within("id", ids))
    where = " WHERE " + " AND ".join(filters) if filters else ""
    columns = "id,name,kind,area,owned,quantity,bottle_label,categories,machine_id,can_take_now," + STOCK_DETAIL_COLUMNS
    if ids is not None:
        columns += ",save_id"
    items = [row_to_item(row, include_categories=True) for row in db.execute("SELECT " + columns + " FROM stock" + where + " ORDER BY id", params)]
    if ids is not None:
        by_id = {item["id"]: item for item in items}
        ordered = dict.fromkeys(ids)
        items = [by_id[i] for i in ordered if i in by_id]
        receipt["missing_ids"] = [i for i in ordered if i not in by_id]
    return {**receipt, "items": items}
