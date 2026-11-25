"""Shared multi-step workflow runner (Flow) and counter receipt presentation chain."""
import time

from command_transport import poll
import counter
import shopdesk


class Flow:
    """Track revision advancement, step records, and early-stop state across multi-step workflows."""

    def __init__(self, folder, generation, revision):
        self.folder = folder
        self.generation = generation
        self.rev = revision
        self.steps = {}

    def advance_rev(self, receipt):
        """Advance tracked revision from a command receipt or fallback desk observation."""
        if isinstance(receipt, dict):
            if isinstance(receipt.get("revision"), int) and receipt.get("generation") in (None, self.generation):
                self.rev = receipt["revision"]
                return self.rev
            update = receipt.get("shop_update") or {}
            if isinstance(update.get("revision"), int):
                self.rev = update["revision"]
                return self.rev
        ctx = counter.context(self.folder)
        if ctx.get("status") == "observed" and ctx.get("generation") == self.generation and isinstance(ctx.get("revision"), int):
            self.rev = ctx["revision"]
        return self.rev

    def sync_revision(self, target_revision=None, *, timeout=1.0):
        """Wait briefly until the published desk reaches at least target_revision."""
        target = self.rev if target_revision is None else target_revision

        def reached():
            view = counter.context(self.folder)
            if view.get("status") != "observed" or view.get("generation") != self.generation or view.get("revision", 0) >= target:
                return True
            return None

        poll(reached, timeout=timeout, interval=0.02, sleep=time.sleep)

    def command(self, action, **args):
        """Execute one counter command at the current revision and advance self.rev."""
        res = counter.command(self.folder, action, self.generation, self.rev, **args)
        self.advance_rev(res)
        return res

    def stop(self, step_name, reason, **extra):
        """Build a standardized stopped payload at the current step and revision."""
        out = {
            "status": "stopped",
            "stopped_at": step_name,
            "reason": reason,
            "steps": self.steps,
            "revision": self.rev,
        }
        out.update(extra)
        return out


def present_counter(view, *, folder, database, summarize_offer, verbose=False,
                    apply_barter=False, strip=False):
    """Apply the standard counter post-processing chain:
    with_barter -> reception_token -> strip_protocols -> summarize_offer -> filter_finished_heat.
    """
    payload = view
    if apply_barter and isinstance(payload, dict):
        payload = counter.with_barter(folder, payload)
    if isinstance(payload, dict) and "reception_token" not in payload:
        reception = payload.get("reception")
        if isinstance(reception, dict) and "token" in reception:
            payload = {**payload, "reception_token": reception["token"]}
    if strip and isinstance(payload, dict):
        payload = shopdesk.strip_protocols(payload)
    payload = summarize_offer(payload, verbose=verbose)
    return counter.filter_finished_heat(database, payload)
