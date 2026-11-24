using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WhiteDragon.Ledger
{
    public sealed class ItemPanelLifetime : MonoBehaviour
    {
        internal int Epoch {get; private set;}
        private void OnDisable() { Epoch++; }
    }

    internal static class NativeItemPanels
    {
        // Only roots supplied by native item UIs. Multiple open roots have no unambiguous action target.
        private sealed class Root
        {
            internal string Key, Route;
            internal GameObject Panel;
            internal GameItem Item;
            internal Root(string key,GameObject panel,string route=null,GameItem item=null)
            { Key=key; Panel=panel; Route=route; Item=item; }
        }

        private static IEnumerable<Root> Roots()
        {
            var custom=CustomUIManager.Instance;
            if (custom!=null)
                foreach (var key in new[] { "intake_hopper","outlet_hopper","storage_coordinator","mind_reprogrammer" })
                    yield return new Root(key,custom.GetWindow(key)?.Rect?.gameObject);
            yield return new Root("feature_labels",LabelerUIManager.Instance?.panel);
            yield return new Root("custom_name",LabelerBlueUIManager.Instance?.panel);
            yield return new Root("plant_analyser",PlantAnalyserUIManager.Instance?.UIPanel,null,PlantAnalyserUIManager.Instance?.currentItem);
            yield return new Root("chemical_finisher",ChemFinisherUIManager.Instance?.UIPanel,null,ChemFinisherUIManager.Instance?.currentItem);
            yield return new Root("chemical_scanner",ChemScannerUIManager.Instance?.panel,null,ChemScannerUIManager.Instance?.currentScannedItem);
            yield return new Root("water_scanner",WaterScannerUIManager.Instance?.panel,null,WaterScannerUIManager.Instance?.currentWaterContainer);
            yield return new Root("security_record",RecordUIManager.Instance?.UIPanel);
            yield return new Root("loan",LoanPanelUIManager.Instance?.panel);
            yield return new Root("exchange_list",ExchangeUIManager.Instance?.panel);
            yield return new Root("newspaper",NewsUIManager.Instance?.uiGameObject);
            yield return new Root("note",HandnoteUIManager.Instance?.panel);
            yield return new Root("pages",MultiPageUIManager.Instance?.panel);
            yield return new Root("paper",PaperUIManager.Instance?.panel);
            yield return new Root("inspection",InspectionUIManager.Instance?.panel,"player_visual_check");
            yield return new Root("logo_reference",RepublicLogoUIManager.Instance?.panel,"player_visual_check");
            yield return new Root("water_strip",WaterStripUIManager.Instance?.panel,"water_panel");
            yield return new Root("scratch_card",ScratcherUIManager.Instance?.ui,"scratch_card");
        }

        private static ItemPanelLifetime ownedConfirmation;
        private static int confirmationEpoch;

        internal static bool ConfirmationOpen => GenericConfirmationUIManager.Instance?.panel?.activeInHierarchy==true;
        internal static void Reset() => ownedConfirmation=null;

        private static ItemPanelLifetime Lifetime(GameObject panel) =>
            panel.GetComponent<ItemPanelLifetime>() ?? panel.AddComponent<ItemPanelLifetime>();

        internal static void CaptureConfirmation(bool previouslyOpen)
        {
            if (previouslyOpen || !ConfirmationOpen) return;
            ownedConfirmation=Lifetime(GenericConfirmationUIManager.Instance.panel);
            confirmationEpoch=ownedConfirmation.Epoch;
        }

        internal static string ActiveKey()
        {
            if (GenericConfirmationUIManager.Instance?.panel?.activeInHierarchy==true) return "confirmation";
            string keys=string.Join(",",Roots().Where(r=>r.Panel!=null && r.Panel.activeInHierarchy).Select(r=>r.Key));
            return keys.Length==0 ? null : keys;
        }

        internal static ItemPanelView Current(string requested=null)
        {
            List<Root> roots;
            if (ConfirmationOpen)
            {
                var dialog=GenericConfirmationUIManager.Instance.panel;
                if (ownedConfirmation==null || ownedConfirmation.gameObject!=dialog || ownedConfirmation.Epoch!=confirmationEpoch)
                    return new ItemPanelView { Key="confirmation", DelegateTo="player_confirmation" };
                roots=new List<Root> { new Root("confirmation",dialog) };
            }
            else
            {
                var active=Roots().Where(r=>r.Panel!=null && r.Panel.activeInHierarchy).ToList();
                roots=active.Where(r=>!active.Any(other=>other!=r && other.Panel.transform.IsChildOf(r.Panel.transform))).ToList();
            }
            if (roots.Count==0) return null;
            var root=requested==null ? (roots.Count==1 ? roots[0] : null) : roots.FirstOrDefault(r=>r.Key==requested);
            if (root==null) return new ItemPanelView { Key=string.Join(",",roots.Select(r=>r.Key)), DelegateTo="select_panel" };
            if (root.Route!=null) return new ItemPanelView { Key=root.Key, DelegateTo=root.Route };
            var lifetime=Lifetime(root.Panel);
            var view=new ItemPanelView { Key=root.Key,
                Identity=root.Key+"/"+root.Panel.GetInstanceID()+"/"+lifetime.Epoch+"/"+root.Item?.GetUniqueID() };
            switch (root.Key)
            {
                case "chemical_scanner": view.Close=ChemScannerUIManager.Instance.Close; break;
                case "chemical_finisher": view.Close=ChemFinisherUIManager.Instance.HideUI; break;
                case "plant_analyser": view.Close=PlantAnalyserUIManager.Instance.HideUI; break;
                case "water_scanner": view.Close=WaterScannerUIManager.Instance.Close; break;
            }
            if (root.Key=="chemical_scanner") view.Scan=ScannerReading();
            if (root.Key!="confirmation") view.Focus=()=>PaperPanelFocusStamp.BumpPanel(root.Panel);
            foreach (var label in root.Panel.GetComponentsInChildren<TMP_Text>())
                if (label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text)) view.Text.Add(label.text.Trim());
            foreach (var label in root.Panel.GetComponentsInChildren<Text>())
                if (label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text)) view.Text.Add(label.text.Trim());
            foreach (var control in root.Panel.GetComponentsInChildren<Selectable>())
                if (control.isActiveAndEnabled) Add(view,control);
            return view;
        }

        private static readonly System.Text.RegularExpressions.Regex Percent =
            new System.Text.RegularExpressions.Regex(@"(\d+(?:\.\d+)?)\s*%");

        // The scanner's last text line (after the bracketed heading and any percentage) is its grade wording.
        private static JObject ScannerLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lines=text.Split(new[]{'\n'}).Select(l=>l.Trim()).Where(l=>l.Length>0 && !l.StartsWith("[")).ToList();
            var row=new JObject();
            var match=Percent.Match(text);
            if (match.Success) row["percent"]=double.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
            var words=lines.LastOrDefault(l=>!Percent.IsMatch(l));
            if (words!=null) row["display"]=words;
            return row.HasValues ? row : null;
        }

        // Same components the native scanner lists: every known liquid part with a positive amount on the item.
        internal static JObject ScannerReading()
        {
            var ui=ChemScannerUIManager.Instance;
            var item=ui?.currentScannedItem;
            if (item==null) return null;
            var parts=LiquidParts.Read(item);
            long total=parts.Sum(p=>(long)p.Value);
            var rows=new JArray();
            foreach (var part in parts)
            {
                int ul=part.Value;
                rows.Add(new JObject { ["id"]=part.Key.identifier, ["name"]=part.Key.displayName, ["ul"]=ul,
                    ["ml"]=Math.Round(ul/1000.0,3), ["percent"]=total==0 ? 0 : Math.Round(ul*100.0/total,2) });
            }
            var scan=new JObject { ["item_name"]=item.GetDisplayName(true), ["kind"]=item.identifier,
                ["total_ml"]=Math.Round(total/1000.0,3), ["components"]=rows };
            if (ScannerLine(ui.testedTMP?.text) is JObject tested) scan["tested"]=tested;
            if (ScannerLine(ui.labelTMP?.text) is JObject label) scan["label"]=label;
            var effects=new JObject { ["narcotic"]=ChemicalFeedbackHelper.IsNarcotic(item),
                ["euphoria_level"]=ChemicalFeedbackHelper.GetEuphoriaLevel(item),
                ["toxic"]=ChemicalFeedbackHelper.IsToxic(item), ["lethal"]=ChemicalFeedbackHelper.IsLethal(item),
                ["synaprest"]=ChemicalFeedbackHelper.IsSynaprest(item), ["birth_control"]=ChemicalFeedbackHelper.IsBirthControl(item) };
            if ((bool)effects["lethal"]) effects["lethal_dose"]=ChemicalFeedbackHelper.GetLethalDose(item);
            scan["effects"]=effects;
            return scan;
        }

        private static bool Reachable(Selectable control)
        {
            var rect=(RectTransform)control.transform;
            var canvas=control.GetComponentInParent<Canvas>();
            if (canvas==null || EventSystem.current==null) return false;
            var point=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            return hits.Count>0 && hits[0].gameObject.transform.IsChildOf(rect);
        }

        private static void Add(ItemPanelView view,Selectable control)
        {
            var row=new JObject();
            Func<JToken,string> action;
            var caption=control.GetComponentInChildren<TMP_Text>()?.text ?? control.GetComponentInChildren<Text>()?.text;
            row["label"]=string.IsNullOrWhiteSpace(caption) ? control.name : caption;
            if (control is TMP_Dropdown dropdown)
            {
                row["type"]="choice"; row["value"]=dropdown.value;
                row["choices"]=new JArray(dropdown.options.Select(o=>o.text));
                var label=dropdown.GetComponentInParent<LabelerElement>();
                if (label!=null) row["label"]=label.featureName.text;
                action=value=>{
                    if (value?.Type!=JTokenType.Integer || (long)value<0 || (long)value>=dropdown.options.Count) return "invalid_choice";
                    dropdown.value=(int)value; return "applied";
                };
            }
            else if (control is TMP_InputField input && view.Key=="custom_name" && input==LabelerBlueUIManager.Instance.inputField)
            {
                row["type"]="text"; row["value"]=input.text; row["limit"]=input.characterLimit;
                action=value=>{
                    if (value?.Type!=JTokenType.String || (input.characterLimit>0 && ((string)value).Length>input.characterLimit)) return "invalid_text";
                    input.text=(string)value;
                    LabelerBlueUIManager.Instance.OnEditEnd(input.text);
                    return "applied";
                };
            }
            else if (control is TMP_InputField field)
            {
                row["type"]="text"; row["value"]=field.text; row["limit"]=field.characterLimit;
                action=value=>{
                    if (field.readOnly || value?.Type!=JTokenType.String || (field.characterLimit>0 && ((string)value).Length>field.characterLimit))
                        return "invalid_text";
                    field.text=(string)value;
                    field.onEndEdit.Invoke(field.text);
                    return "applied";
                };
            }
            else if (control is Slider slider)
            {
                row["type"]="slider"; row["value"]=slider.value; row["min"]=slider.minValue; row["max"]=slider.maxValue;
                row["whole_numbers"]=slider.wholeNumbers;
                action=value=>{
                    if (value?.Type!=JTokenType.Integer && value?.Type!=JTokenType.Float) return "invalid_value";
                    double number=(double)value;
                    if (!(number>=slider.minValue && number<=slider.maxValue) || (slider.wholeNumbers && Math.Truncate(number)!=number))
                        return "invalid_value";
                    slider.value=(float)number;
                    return "applied";
                };
            }
            else if (control is Toggle toggle)
            {
                row["type"]="toggle"; row["value"]=toggle.isOn;
                action=value=>{ if (value?.Type!=JTokenType.Boolean) return "invalid_value"; toggle.isOn=(bool)value; return "applied"; };
            }
            else if (control is Button button)
            {
                row["type"]="button";
                if (view.Key=="chemical_finisher")
                {
                    var chem=button.GetComponentInParent<ChemFinishElement>();
                    if (chem!=null && !string.IsNullOrEmpty(chem.identifier))
                    {
                        row["recipe_id"]=chem.identifier;
                        var recipe=ChemFinisherRecipe.GetRecipeById(chem.identifier);
                        row["recipe_name"]=recipe?.displayName ?? chem.displayTMP?.text;
                        if (recipe!=null)
                        {
                            row["required_ul"]=recipe.requiredVolume;
                            row["required_ml"]=Math.Round(recipe.requiredVolume/1000.0,3);
                            row["requires_dry"]=recipe.isRequireDry;
                        }
                    }
                }
                action=value=>{ if (value!=null && value.Type!=JTokenType.Null) return "unexpected_value"; button.onClick.Invoke(); return "applied"; };
            }
            else if (control is Scrollbar scroll)
            {
                row["type"]="scroll"; row["value"]=Math.Round(scroll.value,3);
                action=value=>{ if (value?.Type!=JTokenType.Float && value?.Type!=JTokenType.Integer) return "invalid_value";
                    double position=(double)value; if (!(position>=0 && position<=1)) return "invalid_value";
                    scroll.value=(float)position; return "applied"; };
            }
            else return;
            string key=control.GetInstanceID().ToString();
            bool enabled=control.IsInteractable() && !(control is TMP_InputField readOnly && readOnly.readOnly) && Reachable(control);
            row["enabled"]=enabled;
            view.Controls[key]=row;
            if (enabled) view.Actions[key]=action;
        }
    }
}
