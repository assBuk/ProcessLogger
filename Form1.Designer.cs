using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace ProcessMonitor
{
    partial class Form1 : Form // Явное указание наследования от Form
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        // Остальная часть вашего кода...
    }
}
