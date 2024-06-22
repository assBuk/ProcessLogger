using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcessLogger
{
    public partial class Form1 : Form
    {
        private RichTextBox logBox = new RichTextBox();
        private Process[] initialProcesses;
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        private string logFilePath;
        private MenuItem alwaysOnTopMenuItem;
        private bool alwaysOnTop = false;

        public Form1(string[] args)
        {
            string username = WindowsIdentity.GetCurrent().Name.Replace("\\", "_");
            logFilePath = $"{DateTime.Now:yyyy_MM_dd-HH_mm_ss}({username}).log";

            bool trayMode = args.Contains("-tray");
            bool silentMode = args.Contains("/silent");
            //Агрументы запуска
            if (silentMode || trayMode)
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Visible = false;
                InitComponents();
                SubscribeToProcessEvents(); 
            }
            else
            {
                InitComponents();
                this.Load += MonitoringForm_Load;
                SubscribeToProcessEvents();
                LogSystemInformation();
            }
            LogSystemInformation();
        }

        private void InitComponents()
        {
            //Местный дизайнер.
            this.Text = "Мониторинг действий";
            this.Size = new System.Drawing.Size(800, 600);
            this.TransparencyKey = Color.FromArgb(0, 0, 1);
            this.BackColor = Color.FromArgb(0, 0, 1); 
            this.Opacity = 0.85; 
            logBox.Dock = DockStyle.Fill;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.Black;
            logBox.ForeColor = Color.White;
            this.Controls.Add(logBox);

            // Сейвим список текущих процессов для сравнения
            initialProcesses = Process.GetProcesses();

            // Трей
            CreateTrayIcon();
        }

        private void CreateTrayIcon()
        {
            trayMenu = new ContextMenu();
            alwaysOnTopMenuItem = new MenuItem("Поверх всех окон", ToggleAlwaysOnTop);
            trayMenu.MenuItems.Add(alwaysOnTopMenuItem);
            trayMenu.MenuItems.Add("Открыть папку программы", OpenProgramFolder);
            trayMenu.MenuItems.Add("Открыть текущий лог файл", OpenLogFile);
            trayMenu.MenuItems.Add("Выход", OnExit);
            trayIcon = new NotifyIcon();
            trayIcon.Text = "Мониторинг действий";
            trayIcon.Icon = new Icon(SystemIcons.Application, 40, 40);
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += new EventHandler(TrayIcon_DoubleClick);
            this.Resize += new EventHandler(Form1_Resize);

        }

        private void ToggleAlwaysOnTop(object sender, EventArgs e)
        {
            //Поверх всех окон.
            alwaysOnTop = !alwaysOnTop;
            this.TopMost = alwaysOnTop;
            alwaysOnTopMenuItem.Checked = alwaysOnTop;
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                trayIcon.Visible = true;
                ShowInTaskbar = false;
            }
            else if (WindowState == FormWindowState.Normal)
            {
                Show();
                trayIcon.Visible = false;
                ShowInTaskbar = true;
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            trayIcon.Visible = false;
        }

        private void SubscribeToProcessEvents()
        {
            try
            {
                ManagementEventWatcher startWatch = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                startWatch.EventArrived += new EventArrivedEventHandler(ProcessStarted);
                startWatch.Start();

                ManagementEventWatcher stopWatch = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
                stopWatch.EventArrived += new EventArrivedEventHandler(ProcessStopped);
                stopWatch.Start();
            }
            catch (ManagementException ex)
            {
                string errorMessage = $"Произошла ошибка при подписке на события процессов: {ex.Message}";
                AppendLog(errorMessage, Color.Red);
                LogToFile(errorMessage);
            }
        }

        private void MonitoringForm_Load(object sender, EventArgs e)
        {
            LogSystemInformation();
            Timer timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += Timer_Tick;
            timer.Start();

        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var currentProcesses = Process.GetProcesses();
            foreach (var process in currentProcesses)
            {
                if (!Array.Exists(initialProcesses, p => p.Id == process.Id))
                {
                    LogProcessStart(process);
                }
            }
            foreach (var initialProcess in initialProcesses)
            {
                if (!Array.Exists(currentProcesses, p => p.Id == initialProcess.Id))
                {
                    LogProcessEnd(initialProcess);
                }
            }
            initialProcesses = currentProcesses;
        }

        private void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            var processId = (uint)e.NewEvent.Properties["ProcessID"].Value;
            var processName = (string)e.NewEvent.Properties["ProcessName"].Value;
            string parentProcessName = GetParentProcessName((int)processId);
            string args = GetProcessCommandLineArgs((int)processId);
            string message = $"[{DateTime.Now}] ЗАПУЩЕН: {processName} (ID: {processId})";
            if (!string.IsNullOrEmpty(args))
                message += $" Аргументы: {args}";
            if (!string.IsNullOrEmpty(parentProcessName))
                message += $" Родительский процесс: {parentProcessName}";
            AppendLog(message, Color.Lime);
            LogToFile(message);
        }

        private void LogProcessStart(Process process)
        {
            string message = $"[{DateTime.Now}] Процесс запущен: {process.ProcessName} (ID: {process.Id})";
            AppendLog(message, Color.Green);
            LogToFile(message);
        }

        private void LogProcessEnd(Process process)
        {
            string message = $"[{DateTime.Now}] Процесс завершен: {process.ProcessName} (ID: {process.Id})";
            AppendLog(message, Color.Red);
            LogToFile(message);
        }

        private void ProcessStopped(object sender, EventArrivedEventArgs e)
        {
            var processId = (uint)e.NewEvent.Properties["ProcessID"].Value;
            var processName = (string)e.NewEvent.Properties["ProcessName"].Value;
            string message = $"[{DateTime.Now}] Закрыт: {processName} (ID: {processId})";
            AppendLog(message, Color.Red);
            LogToFile(message);
        }

        private void AppendLog(string text, Color color)
        {
            if (logBox.IsHandleCreated)
            {
                logBox.Invoke(new Action(() =>
                {
                    logBox.SelectionColor = color;
                    logBox.AppendText($"{text}\n");
                    logBox.SelectionStart = logBox.TextLength;
                    logBox.ScrollToCaret();
                }));
            }
        }

        private void LogToFile(string message)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(logFilePath, true))
                {
                    writer.WriteLine($"{message}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при записи в лог-файл: {ex.Message}", Color.Red);
            }
        }
        private void OpenProgramFolder(object sender, EventArgs e)
        {
            string folderPath = AppDomain.CurrentDomain.BaseDirectory;
            Process.Start("explorer.exe", folderPath);
        }

        private void OpenLogFile(object sender, EventArgs e)
        {
            Process.Start("notepad.exe", logFilePath);
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            System.Windows.Forms.Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        private string GetParentProcessName(int pid)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId = {pid}"))
                {
                    var processList = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (processList != null)
                    {
                        var parentId = (uint)processList["ParentProcessId"];
                        using (var parentSearcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId = {parentId}"))
                        {
                            var parentProcessList = parentSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                            if (parentProcessList != null)
                            {
                                return (string)parentProcessList["Name"];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении информации о родительском процессе: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении информации о родительском процессе: {ex.Message}");
            }
            return null;
        }

        private string GetProcessCommandLineArgs(int pid)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}"))
                {
                    var processList = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                    if (processList != null)
                    {
                        return (string)processList["CommandLine"];
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении командной строки процесса: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении командной строки процесса: {ex.Message}");
            }
            return null;
        }
        private string GetOperatingSystemVersion()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption, Version FROM Win32_OperatingSystem");
                ManagementObjectCollection osCollection = searcher.Get();

                foreach (ManagementObject obj in osCollection)
                {
                    string caption = obj["Caption"] as string;
                    string version = obj["Version"] as string;
                    return $"Операционная система: {caption} (версия {version})";
                }

                return "Не удалось определить операционную систему";
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении информации об операционной системе: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении информации об операционной системе: {ex.Message}");
                return "Не удалось определить операционную систему";
            }
        }
        private string GetSystemInstallationDate()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem");
                ManagementObjectCollection osCollection = searcher.Get();

                foreach (ManagementObject obj in osCollection)
                {
                    string installDate = obj["InstallDate"] as string;
                    if (!string.IsNullOrEmpty(installDate) && installDate.Length >= 14)
                    {
                        string year = installDate.Substring(0, 4);
                        string month = installDate.Substring(4, 2);
                        string day = installDate.Substring(6, 2);

                        DateTime installDateTime = new DateTime(int.Parse(year), int.Parse(month), int.Parse(day));
                        return $"Дата установки системы: {installDateTime.ToShortDateString()}";
                    }
                }

                return "Дата установки системы: Неизвестно";
            }
            catch (Exception ex)
            {
                return $"Ошибка при получении даты установки системы: {ex.Message}";
            }
        }
        private string GetSystemUptime()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
                ManagementObjectCollection osCollection = searcher.Get();

                foreach (ManagementObject obj in osCollection)
                {
                    DateTime lastBootUpTime = ManagementDateTimeConverter.ToDateTime(obj["LastBootUpTime"].ToString());
                    TimeSpan uptime = DateTime.Now - lastBootUpTime;
                    return $"Время работы системы: {uptime.Days} дней, {uptime.Hours} часов, {uptime.Minutes} минут, {uptime.Seconds} секунд";
                }

                return "Не удалось определить время работы системы";
            }
            catch (Exception ex)
            {
                return $"Ошибка при получении времени работы системы: {ex.Message}";
            }
        }
        public static DateTime GetLastShutdownTime()
        {
            try
            {
                ManagementObjectSearcher mos = new ManagementObjectSearcher(@"root\CIMV2", "SELECT LastShutdownTime FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in mos.Get())
                {
                    DateTime lastShutdown = ManagementDateTimeConverter.ToDateTime(mo["LastShutdownTime"].ToString());
                    return lastShutdown;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении даты последнего выключения системы: {ex.Message}");
            }
            return DateTime.MinValue; // Сам добавь
        }
        private string GetRAMInfo()
        {
            string ramInfo = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
                ManagementObjectCollection collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    ulong capacity = Convert.ToUInt64(obj["Capacity"]);
                    uint speed = Convert.ToUInt32(obj["Speed"]);
                    string manufacturer = obj["Manufacturer"]?.ToString();
                    string partNumber = obj["PartNumber"]?.ToString();

                    ramInfo += $"Модуль памяти: {manufacturer} {partNumber}\n";
                    ramInfo += $"Объем: {capacity / (1024 * 1024)} MB\n";
                    ramInfo += $"Скорость: {speed} MHz\n\n";
                }
            }
            catch (Exception ex)
            {
                ramInfo = $"Ошибка при получении информации о RAM: {ex.Message}";
                AppendLog(ramInfo, Color.Red);
                LogToFile(ramInfo);
            }
            return ramInfo;
        }
        private string GetCurrentNetworkAdapter()
        {
            string adapterInfo = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Description FROM Win32_NetworkAdapter WHERE NetConnectionStatus = 2");
                ManagementObjectCollection collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    string adapterName = obj["Description"]?.ToString();
                    if (!string.IsNullOrEmpty(adapterName))
                    {
                        adapterInfo += $"Текущий сетевой адаптер: {adapterName}\n";
                    }
                }
            }
            catch (Exception ex)
            {
                adapterInfo = $"Ошибка при получении информации о сетевом адаптере: {ex.Message}";
                AppendLog(adapterInfo, Color.Red);
                LogToFile(adapterInfo);
            }
            return adapterInfo;
        }
        public static string GetStorageDrivesInfo()
        {
            try
            {
                var drivesInfo = new StringBuilder();

                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wmic.exe",
                        Arguments = "diskdrive get model,size,status,FirmwareRevision,index,Partitions,Signature",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                })
                {
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    var lines = Regex.Split(output, "\r\n|\r|\n")
                                     .Where(line => !string.IsNullOrWhiteSpace(line))
                                     .Skip(1) // Пропускаем заголовок
                                     .ToArray();

                    foreach (var line in lines)
                    {
                        // Используем региX для разделения полей
                        var fields = Regex.Split(line.Trim(), @"\s{2,}");
                        if (fields.Length >= 7) //  у нас достаточно полей?
                        {
                            var firmwareRevision = fields[0];
                            var index = fields[1];
                            var model = fields[2];
                            var partitions = fields[3];
                            var signature = fields[4];
                            var size = ConvertToHumanReadableSize(fields[5]);
                            var status = fields[6];
                            drivesInfo.AppendLine($"{model} ({size}) - Статус: {status}, Версия прошивки: {firmwareRevision}, Индекс: {index}, Разделы: {partitions}, Подпись: {signature}");
                        }
                    }
                }return drivesInfo.ToString();
            }
            catch (Exception ex)
            {return $"Ошибка при получении информации о накопителях: {ex.Message}";}
        }
        private static string ConvertToHumanReadableSize(string sizeInBytes)
        {
            if (double.TryParse(sizeInBytes, out double size))
            {
                if (size < 1024) return $"{size} B";
                if (size < 1048576) return $"{Math.Round(size / 1024, 2)} KB";
                if (size < 1073741824) return $"{Math.Round(size / 1048576, 2)} MB";
                return $"{Math.Round(size / 1073741824, 2)} GB";
            }
            return "Хз чё за размер....";
        }
        private void LogSystemInformation()
        {
            try
            {
                string hwid = GetHardwareId();
                string hwid2 = GetHardwareId2();
                string guid = GetSystemGUID();
                string processorNumbers = GetProcessorNumbers();
                string motherboardSerial = GetMotherboardSerial();
                string biosSerial = GetBiosSerial();
                string cpuSerial = GetCpuSerial();
                string videoDriverInfo = GetVideoDriverInfo();
                string osVersion = GetOperatingSystemVersion();
                string installationDate = GetSystemInstallationDate();
                string uptime = GetSystemUptime();
                string ramInfo = GetRAMInfo();
                string networkAdapterInfo = GetCurrentNetworkAdapter();
                string text1 =  Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop\\WindowMetrics").GetValue("AppliedDPI").ToString();
                string storageDrivesTypeInfo = GetStorageDrivesInfo(); 
                string systemInfo = $"HWID: {hwid} | HWID2: {hwid2}\n" +
                                    $"GUID: {guid}\n" +
                                    $"Номера процессоров: {processorNumbers}\n" +
                                    $"Серийный номер материнской платы: {motherboardSerial}\n" +
                                    $"Серийный номер BIOS: {biosSerial}\n" +
                                    $"Серийный номер ЦП: {cpuSerial}\n" +
                                    $"Информация о драйвере видеокарты: {videoDriverInfo}\n" +
                                    $"{osVersion}\n" +
                                    $"{installationDate}\n" +
                                    $"{uptime}\n" +
                                    $"{text1}\n" +
                                    $"Оперативная память:\n{ramInfo}\n" +
                                    $"Тип накопителей:\n{storageDrivesTypeInfo}\n" + 
                                    $"Сетевой адаптер:\n{networkAdapterInfo}\n";
                AppendLog(systemInfo, Color.White);
                LogToFile(systemInfo);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Ошибка при получении информации о системе: {ex.Message}";
                AppendLog(errorMessage, Color.Red);
                LogToFile(errorMessage);
            }
        }
        private string GetHardwareId2()
        {
            try
            {
                string biosSerial = GetBiosSerial();
                string cpuSerial = GetCpuSerial(); 
                string hardwareId = $"{biosSerial}_{cpuSerial}";

                return hardwareId;
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении Hardware ID: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении Hardware ID: {ex.Message}");
                return string.Empty;
            }
        }
        private string GetHardwareId()
        {
            try
            {
                string hddSerial = GetHddSerialNumber(); 
                string processorId = GetProcessorId();  
                string hardwareId = $"{hddSerial}_{processorId}";

                return hardwareId;
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении Hardware ID: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении Hardware ID: {ex.Message}");
                return string.Empty;
            }
        }
        private string GetHddSerialNumber()
        {
            try
            {
                // Получение серийного номера жесткого диска
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE MediaType='Fixed hard disk media'");
                ManagementObjectCollection collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    if (obj["SerialNumber"] != null)
                    {
                        return obj["SerialNumber"].ToString().Trim();
                    }
                }

                return "Не удалось получить серийный номер жесткого диска";
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении серийного номера жесткого диска: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении серийного номера жесткого диска: {ex.Message}");
                return string.Empty;
            }
        }
        private string GetProcessorId()
        {
            try
            {
                // Получение идентификатора процессора
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                ManagementObjectCollection collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    if (obj["ProcessorId"] != null)
                    {
                        return obj["ProcessorId"].ToString().Trim();
                    }
                }

                return "Не удалось получить идентификатор процессора";
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении идентификатора процессора: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении идентификатора процессора: {ex.Message}");
                return string.Empty;
            }
        }
        private string GetSystemGUID()
        {
            string systemGuid = string.Empty;
            try
            {
                systemGuid = System.Guid.NewGuid().ToString(); //Сам придумай чёнить
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении GUID: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении GUID: {ex.Message}");
            }
            return systemGuid;
        }
        private string GetProcessorNumbers()
        {
            string processorNumbers = string.Empty;
            try
            {
                processorNumbers = Environment.ProcessorCount.ToString();
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении количества процессоров: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении количества процессоров: {ex.Message}");
            }
            return processorNumbers;
        }
        private string GetMotherboardSerial()
        {
            string motherboardSerial = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                ManagementObjectCollection collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    motherboardSerial = obj["SerialNumber"].ToString();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении серийного номера материнской платы: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении серийного номера материнской платы: {ex.Message}");
            }
            return motherboardSerial;
        }
        private string GetBiosSerial()
        {
            string biosSerial = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
                ManagementObjectCollection collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    biosSerial = obj["SerialNumber"].ToString();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении серийного номера BIOS: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении серийного номера BIOS: {ex.Message}");
            }
            return biosSerial;
        }
        private string GetCpuSerial()
        {
            string cpuSerial = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                ManagementObjectCollection collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    cpuSerial = obj["ProcessorId"].ToString();
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка при получении серийного номера CPU: {ex.Message}", Color.Red);
                LogToFile($"Ошибка при получении серийного номера CPU: {ex.Message}");
            }
            return cpuSerial;
        }
        private string GetVideoDriverInfo()
        {
            string videoDriverInfo = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                ManagementObjectCollection collection = searcher.Get();
                foreach (ManagementObject obj in collection)
                {
                    videoDriverInfo += $"Устройство: {obj["Name"]}\n";
                    videoDriverInfo += $"Драйвер версии: {obj["DriverVersion"]}\n";

                    // Получение DeviceID для использования в запросе к реестру
                    string deviceID = obj["DeviceID"].ToString();

                    // Попытка получить дату установки драйвера из реестра
                    RegistryKey registryKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{{{deviceID}}}");
                    if (registryKey != null)
                    {
                        object driverInstallDate = registryKey.GetValue("DriverDesc");
                        if (driverInstallDate != null)
                        {
                            videoDriverInfo += $"Дата установки драйвера: {driverInstallDate}\n";
                        }
                    }

                    videoDriverInfo += $"Видеопроцессор: {obj["VideoProcessor"]}\n";

                    // Получение размера адаптера RAM
                    uint adapterRam = 0;
                    if (uint.TryParse(obj["AdapterRAM"].ToString(), out adapterRam))
                    {
                        videoDriverInfo += $"Адаптер RAM: {adapterRam / (1024 * 1024)} MB\n\n";
                    }
                    else
                    {
                        videoDriverInfo += $"Адаптер RAM: Неизвестно\n\n";
                    }
                }
            }
            catch (Exception ex)
            {
                videoDriverInfo = $"Ошибка при получении информации о видеодрайвере: {ex.Message}";
                AppendLog(videoDriverInfo, Color.Red);
                LogToFile(videoDriverInfo);
            }
            return videoDriverInfo;
        }
    }
    // Классы которые ты можешь дописать.
    //А так поебать... Сами допиши, чё доебался?

}
