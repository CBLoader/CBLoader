﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// 
// This source code was auto-generated by xsd, Version=4.0.30319.1.
// 
namespace CharacterBuilderLoader {
    using System.Xml.Serialization;
    
    
    /// <remarks/>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("xsd", "4.0.30319.1")]
    [System.SerializableAttribute()]
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.ComponentModel.DesignerCategoryAttribute("code")]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="http://code.google.com/p/cbloader/cbconfig.xsd")]
    [System.Xml.Serialization.XmlRootAttribute("Settings", Namespace="http://code.google.com/p/cbloader/cbconfig.xsd", IsNullable=false)]
    public partial class SettingsType {
        
        private string[] foldersField;
        
        private bool fastModeField;
        
        private bool fastModeFieldSpecified;
        
        private string basePathField;
        
        private string cBPathField;
        
        private string keyFileField;
        
        private bool verboseModeField;
        
        private bool verboseModeFieldSpecified;
        
        private bool alwaysRemergeField;
        
        private bool alwaysRemergeFieldSpecified;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlArrayItemAttribute("Custom", IsNullable=false)]
        public string[] Folders {
            get {
                return this.foldersField;
            }
            set {
                this.foldersField = value;
            }
        }
        
        /// <remarks/>
        public bool FastMode {
            get {
                return this.fastModeField;
            }
            set {
                this.fastModeField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool FastModeSpecified {
            get {
                return this.fastModeFieldSpecified;
            }
            set {
                this.fastModeFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        public string BasePath {
            get {
                return this.basePathField;
            }
            set {
                this.basePathField = value;
            }
        }
        
        /// <remarks/>
        public string CBPath {
            get {
                return this.cBPathField;
            }
            set {
                this.cBPathField = value;
            }
        }
        
        /// <remarks/>
        public string KeyFile {
            get {
                return this.keyFileField;
            }
            set {
                this.keyFileField = value;
            }
        }
        
        /// <remarks/>
        public bool VerboseMode {
            get {
                return this.verboseModeField;
            }
            set {
                this.verboseModeField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool VerboseModeSpecified {
            get {
                return this.verboseModeFieldSpecified;
            }
            set {
                this.verboseModeFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        public bool AlwaysRemerge {
            get {
                return this.alwaysRemergeField;
            }
            set {
                this.alwaysRemergeField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool AlwaysRemergeSpecified {
            get {
                return this.alwaysRemergeFieldSpecified;
            }
            set {
                this.alwaysRemergeFieldSpecified = value;
            }
        }
    }
}
