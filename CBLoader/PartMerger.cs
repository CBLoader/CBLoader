using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CBLoader
{
    /// <summary>
    /// A data structure containing the data in a single RulesElement tag.
    /// </summary>
    internal class RulesElement
    {
        private const string ROOT_TEXT_TAG = "$$CBLoader_Root_Text";
        private const string FLAVOR_TAG = "$$CBLoader_Specific_Flavor";
        private const string PREREQS_TAG = "$$CBLoader_Specific_Prereqs";
        private const string PRINT_PREREQS_TAG = "$$CBLoader_Specific_Print_Prereqs";

        private Dictionary<string, string> attributes = new Dictionary<string, string>();
        private StringBuilder rootText = null;
        private HashSet<string> categories = new HashSet<string>();
        private List<XElement> rootElements = new List<XElement>();
        private List<XNode> rules = new List<XNode>();

        internal string InternalId { get => attributes[PartMerger.INTERNAL_ID];
                                     set => attributes[PartMerger.INTERNAL_ID] = value; }

        internal RulesElement(string id)
        {
            InternalId = id;
        }

        private bool isAttributeValid(string attribute)
        {
            bool valid = false;
            switch (attribute)
            {
                case "name": case "type": case "internal-id":
                case "source": case "revision-date":
                    valid = true;
                    break;
            }
            if (!valid)
                Log.Warn($"   - Attempt to set invalid attribute '{attribute}' in '{InternalId}'");
            return valid;
        }

        private XNode cloneNode(XNode node)
        {
            if (node is XText) return new XText((XText) node);
            if (node is XElement) return new XElement((XElement) node);
            throw new Exception("cannot clone node");
        }
        internal XElement MakeElement()
        {
            var element = new XElement("RulesElement");
            foreach (var attribute in attributes)
                element.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);

            if (categories.Count > 0)
            {
                var childElement = new XElement("Category");
                childElement.Add(new XText(String.Join(",", categories.ToArray())));
                element.Add(childElement);
            }
            foreach (var rootElement in rootElements)
                element.Add(new XElement(rootElement));
            if (rules.Count > 0)
            {
                var rulesElement = new XElement("rules");
                foreach (var rule in rules) rulesElement.Add(cloneNode(rule));
                element.Add(rulesElement);
            }
            if (rootText != null)
                element.Add(new XText(rootText.ToString()));
            return element;
        }
        
        private string elementValue(XElement element)
        {
            if (element.FirstNode != null && (!(element.FirstNode is XText) || element.FirstNode.NextNode != null))
                Log.Warn($"   - Tag {element} contains non-text elements!");
            return element.Value;
        }

        private void copyAttributes(XElement element)
        {
            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (isAttributeValid(name))
                    attributes[name] = attribute.Value;
            }
        }
        private void pushRulesNode(XNode node)
        {

            if (node is XElement)
            {
                rules.Add((XElement) node);
                return;
            }
            if (node is XText)
            {
                var text = (XText) node;
                if (text.Value.Trim() == "") return;
                rules.Add(text);
                return;
            }
            Log.Warn($"   - Encountered unknown rules node: {node}");
        }
        private void pushNode(XNode node)
        {
            if (node is XElement)
            {
                var element = (XElement) node;
                switch (element.Name.LocalName.ToLower())
                {
                    case "category":
                        var value = elementValue(element);
                        foreach (var category in value.Split(',').Select(x => x.Trim()).Where(x => x != ""))
                            categories.Add(category);
                        return;
                    case "specific":
                    case "prereqs":
                    case "print-prereqs":
                    case "flavor":
                        rootElements.Add(element);
                        return;
                    case "rules":
                        foreach (var rule in element.Nodes())
                            pushRulesNode(rule);
                        return;
                }
            }

            if (node is XText)
            {
                var text = (XText) node;
                if (rootText == null) rootText = new StringBuilder(text.Value);
                else rootText.Append($"\n{text.Value}");
                return;
            }

            Log.Warn($"   - Encountered unknown RulesElement node: {node}");
        }
        internal void AddNodes(XElement element, bool attributes = true)
        {
            if (attributes) copyAttributes(element);
            foreach (var node in element.Nodes())
                pushNode(node);
        }
        
        private void removeAttribute(string attr)
        {
            if (!attributes.ContainsKey(attr))
                Log.Warn($"   - Attempt to delete non-existant attribute '{attr}' in '{InternalId}'");
            else attributes.Remove(attr);
        }
        private void processRemoveNode(XNode node)
        {
            if (node is XElement)
            {
                var element = (XElement)node;
                switch (element.Name.LocalName.ToLower())
                {
                    case "category":
                        categories.Clear();
                        return;
                    case "specific":
                        var nameAttr = element.Attribute("name");
                        if (nameAttr == null)
                            Log.Warn($"   - Attempt to remove a specific in '{InternalId}' without an name. A name attribute is required.");
                        else rootElements.RemoveAll(x => x.Name.LocalName.ToLower() == "specific" && x.Attribute("name").Value == nameAttr.Value);
                        return;
                    case "prereqs":
                    case "print-prereqs":
                    case "flavor":
                        rootElements.RemoveAll(x => x.Name.LocalName.ToLower() == element.Name.LocalName.ToLower());
                        return;
                    case "maintext":
                        rootText = null;
                        break;
                    case "attribute":
                        removeAttribute(element.Attribute("name").Value);
                        return;
                    case "rules":
                        rules.Clear();
                        break;
                }
            }

            if (node is XText)
            {
                var text = (XText) node;
                if (text.Value.Trim() == "") return;
                Log.Warn(@"   - Text was found in an RemoveNodes element. To remove text from a rule, use <MainText/> instead.");
                return;
            }

            Log.Warn($"   - Encountered unknown RemoveNodes node: {node}");
        }
        internal void RemoveNodes(XElement element)
        {
            foreach (var attribute in element.Attributes())
                if (attribute.Name.LocalName != PartMerger.INTERNAL_ID)
                {
                    Log.Warn($"   - Attributes found in an RemoveNodes tag for '{InternalId}'. " +
                             $"To remove an attribute from a rule, add an <Attribute name=\"{attribute.Name.LocalName}\"> tag instead.");
                    break;
                }
            foreach (var node in element.Nodes())
                processRemoveNode(node);
        }
    }

    /// <summary>
    /// A data structure representing a complete D20Rules set.
    /// </summary>
    internal class PartMerger
    {
        internal const string INTERNAL_ID = "internal-id";
        internal const string GAME_SYSTEM = "game-system";

        private readonly string gameSystem;
        private Dictionary<string, RulesElement> rules = new Dictionary<string, RulesElement>();
        private List<XElement> rawElements = new List<XElement>();

        public PartMerger(string gameSystem)
        {
            this.gameSystem = gameSystem;
        }

        public XDocument MakeDocument()
        {
            var element = new XElement("D20Rules");
            element.SetAttributeValue(GAME_SYSTEM, gameSystem);
            foreach (var rule in rules)
                element.Add(rule.Value.MakeElement());
            foreach (var rawElement in rawElements)
                element.Add(new XElement(rawElement));
            return new XDocument(element);
        }

        private RulesElement getRules(string internalId, bool overwrite)
        {
            if (overwrite || !rules.ContainsKey(internalId))
            {
                var element = new RulesElement(internalId);
                rules[internalId] = element;
                return element;
            }
            else return rules[internalId];
        }
        private string internalId(XElement element)
        {
            var attr = element.Attribute(INTERNAL_ID);
            if (attr == null)
                throw new CBLoaderException($"No internal-id found in node: {element}");
            return attr.Value;
        }
        private void processElement(XElement element)
        {
            switch (element.Name.LocalName.ToLower())
            {
                case "ruleselement":
                    getRules(internalId(element), true).AddNodes(element);
                    break;
                case "appendnodes":
                    getRules(internalId(element), false).AddNodes(element);
                    break;
                case "massappend":
                    foreach (var id in element.Attribute("ids").Value.Split(',').Select(x => x.Trim()).Where(x => x != ""))
                        if (!rules.ContainsKey(id))
                            Log.Warn($"   - Attempt to mass append to non-existant RulesElement '{id}'");
                        else getRules(id, false).AddNodes(element, attributes: false);
                    break;
                case "removenodes":
                    var removeId = internalId(element);
                    if (!rules.ContainsKey(removeId))
                        Log.Warn($"   - Attempt to remove nodes from non-existant RulesElement '{removeId}'");
                    else getRules(removeId, false).RemoveNodes(element);
                    break;
                case "deleteelement":
                    var deleteId = internalId(element);
                    if (!rules.ContainsKey(deleteId))
                        Log.Warn($"   - Attempt to delete non-existant RulesElement '{deleteId}'");
                    else rules.Remove(deleteId);
                    break;
                case "appendrawelements":
                    rawElements.AddRange(element.Elements());
                    break;
                case "changelog": case "updateinfo":
                    break;
                default:
                    Log.Warn($"   - Encountered unknown D20Rules element: {element}");
                    break;
            }
        }

        public void ProcessDocument(XDocument document)
        {
            if (document.Root.Name.LocalName.ToLower() != "d20rules")
                throw new CBLoaderException("Part file does not have a D20Rules element at its root.");
            foreach (var element in document.Root.Elements())
                processElement(element);
        }
    }
}
