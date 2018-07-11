using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CBLoader
{
    internal class RulesElement
    {
        private const string ROOT_TEXT_TAG = "$$CBLoader_Root_Text";
        private const string FLAVOR_TAG = "$$CBLoader_Specific_Flavor";
        private const string PREREQS_TAG = "$$CBLoader_Specific_Prereqs";
        private const string PRINT_PREREQS_TAG = "$$CBLoader_Specific_Print_Prereqs";

        private Dictionary<string, string> attributes = new Dictionary<string, string>();
        private HashSet<string> categories = new HashSet<string>();
        private Dictionary<string, string> specific = new Dictionary<string, string>();
        private List<XNode> rules = new List<XNode>();

        internal string InternalId { get => attributes[RulesMerger.INTERNAL_ID];
                                     set => attributes[RulesMerger.INTERNAL_ID] = value; }

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
                Log.Warn($"    - Attempt to set invalid attribute '{attribute}' in '{InternalId}'");
            return valid;
        }

        private XNode cloneNode(XNode node)
        {
            if (node is XText) return new XText((XText) node);
            if (node is XElement) return new XElement((XElement) node);
            throw new Exception("cannot clone node");
        }
        private void pushElement(XElement element, string tag, string text)
        {
            var childElement = new XElement(tag);
            childElement.Add(new XText(text));
            element.Add(childElement);
        }
        internal XElement MakeElement()
        {
            var element = new XElement("RulesElement");
            foreach (var attribute in attributes)
                element.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);

            if (categories.Count > 0)
                pushElement(element, "Category", string.Join(",", categories.ToArray()));
            if (specific.ContainsKey(FLAVOR_TAG))
                pushElement(element, "Flavor", specific[FLAVOR_TAG]);
            if (specific.ContainsKey(PREREQS_TAG))
                pushElement(element, "Prereqs", specific[PREREQS_TAG]);
            if (specific.ContainsKey(PRINT_PREREQS_TAG))
                pushElement(element, "print-prereqs", specific[PRINT_PREREQS_TAG]);
            foreach (var specific in specific)
                switch (specific.Key)
                {
                    case ROOT_TEXT_TAG: case FLAVOR_TAG: case PREREQS_TAG:
                    case PRINT_PREREQS_TAG:
                        break;
                    default:
                        var specificElement = new XElement("specific");
                        specificElement.SetAttributeValue("name", specific.Key);
                        specificElement.Add(new XText(specific.Value));
                        element.Add(specificElement);
                        break;
                }
            if (rules.Count > 0)
            {
                var rulesElement = new XElement("rules");
                foreach (var rule in rules) rulesElement.Add(cloneNode(rule));
                element.Add(rulesElement);
            }
            if (specific.ContainsKey(ROOT_TEXT_TAG))
                element.Add(new XText(specific[ROOT_TEXT_TAG]));
            return element;
        }

        private void setSpecific(string key, string data)
        {
            specific[key] = data.Trim();
        }
        private string elementValue(XElement element)
        {
            if (element.FirstNode != null && (!(element.FirstNode is XText) || element.FirstNode.NextNode != null))
                Log.Warn($"    - Tag {element} contains non-text elements!");
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
            Log.Warn($"    - Encountered unknown rules node: {node}");
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
                        setSpecific(element.Attribute("name").Value, elementValue(element));
                        return;
                    case "prereqs":
                        setSpecific(PREREQS_TAG, elementValue(element));
                        return;
                    case "print-prereqs":
                        setSpecific(PRINT_PREREQS_TAG, elementValue(element));
                        return;
                    case "flavor":
                        setSpecific(FLAVOR_TAG, elementValue(element));
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
                var cleaned = text.Value.Trim();
                if (cleaned != "") setSpecific(ROOT_TEXT_TAG, cleaned);
                return;
            }

            Log.Warn($"    - Encountered unknown RulesElement node: {node}");
        }
        internal void AddNodes(XElement element, bool attributes = true)
        {
            if (attributes) copyAttributes(element);
            foreach (var node in element.Nodes())
                pushNode(node);
        }

        private void removeSpecific(string key)
        {
            if (!specific.ContainsKey(key))
                Log.Warn($"    - Attempt to delete non-existant specific '{key}' in '{InternalId}'");
            else specific.Remove(key);
        }
        private void removeAttribute(string attr)
        {
            if (!attributes.ContainsKey(attr))
                Log.Warn($"    - Attempt to delete non-existant attribute '{attr}' in '{InternalId}'");
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
                            Log.Warn($"    - Attempt to remove a specific in '{InternalId}' without an name. A name attribute is required.");
                        else removeSpecific(nameAttr.Value);
                        return;
                    case "prereqs":
                        removeSpecific(PREREQS_TAG);
                        return;
                    case "print-prereqs":
                        removeSpecific(PRINT_PREREQS_TAG);
                        return;
                    case "flavor":
                        removeSpecific(FLAVOR_TAG);
                        return;
                    case "maintext":
                        removeSpecific(ROOT_TEXT_TAG);
                        return;
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
                Log.Warn(@"    - Text was found in an RemoveNodes element. To remove text from a rule, use <MainText/> instead.");
                return;
            }

            Log.Warn($"    - Encountered unknown RemoveNodes node: {node}");
        }
        internal void RemoveNodes(XElement element)
        {
            foreach (var attribute in element.Attributes())
                if (attribute.Name.LocalName != RulesMerger.INTERNAL_ID)
                {
                    Log.Warn($"    - Attributes found in an RemoveNodes tag for '{InternalId}'. " +
                             $"To remove an attribute from a rule, add an <Attribute name=\"{attribute.Name.LocalName}\"> tag instead.");
                    break;
                }
            foreach (var node in element.Nodes())
                processRemoveNode(node);
        }
    }

    internal class RulesMerger
    {
        internal const string INTERNAL_ID = "internal-id";
        internal const string GAME_SYSTEM = "game-system";

        private readonly string gameSystem;
        private Dictionary<string, RulesElement> rules = new Dictionary<string, RulesElement>();
        private List<XElement> rawElements = new List<XElement>();

        public RulesMerger(string gameSystem)
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
                throw new Exception($"No internal-id found in node: {element}");
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
                            Log.Warn($"    - Attempt to mass append to non-existant RulesElement '{id}'");
                        else getRules(id, false).AddNodes(element, attributes: false);
                    break;
                case "removenodes":
                    var removeId = internalId(element);
                    if (!rules.ContainsKey(removeId))
                        Log.Warn($"    - Attempt to remove nodes from non-existant RulesElement '{removeId}'");
                    else getRules(removeId, false).RemoveNodes(element);
                    break;
                case "deleteelement":
                    var deleteId = internalId(element);
                    if (!rules.ContainsKey(deleteId))
                        Log.Warn($"    - Attempt to delete non-existant RulesElement '{deleteId}'");
                    else rules.Remove(deleteId);
                    break;
                case "appendrawelements":
                    rawElements.AddRange(element.Elements());
                    break;
                case "changelog": case "updateinfo":
                    break;
                default:
                    Log.Warn($"    - Encountered unknown D20Rules element: {element}");
                    break;
            }
        }

        private void ProcessDocument(XDocument document)
        {
            if (document.Root.Name.LocalName.ToLower() != "d20rules")
                throw new Exception("Part file does not have a D20Rules element at its root.");
            foreach (var element in document.Root.Elements())
                processElement(element);
        }
        public void ProcessDocument(Stream document) =>
            ProcessDocument(XDocument.Load(XmlReader.Create(document)));
        public void ProcessDocument(string filename) =>
            ProcessDocument(XDocument.Load(filename));
    }
}
