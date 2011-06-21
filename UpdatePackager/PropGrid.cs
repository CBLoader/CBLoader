using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace UpdatePackager
{
    public partial class PropGrid : Form
    {
        public PropGrid(object o)
        {
            InitializeComponent();
            propertyGrid1.SelectedObject = o;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = System.Windows.Forms.DialogResult.OK;
        }
    }
}
