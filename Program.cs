using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcessLogger
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Process currentProcess = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);

            if (processes.Length > 1)
            {
                MessageBox.Show("Дополнительная копия программы уже запущена.", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string[] args = Environment.GetCommandLineArgs();

            // Определяем порядковый номер копии программы
            int copyNumber = processes.Length; 

            Form1 form1 = new Form1(args);
            form1.Text = $"Мониторинг действий ({copyNumber})";
            Application.Run(form1);
        }
    }
}
