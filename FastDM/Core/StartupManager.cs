using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace FastDM
{
    public enum StartupState { Off, On, DisabledByUser, DisabledByPolicy, Unavailable }

    // "Launch at startup (minimized)"
    //   Store (MSIX) বিল্ড: Package.appxmanifest-এর StartupTask (রেজিস্ট্রির Run কী প্যাকেজড অ্যাপে কাজ করে না)
    //   Portable বিল্ড     : HKCU\...\Run (আর্গুমেন্ট --minimized)
    public static class StartupManager
    {
        // Package.appxmanifest-এর TaskId-র সাথে হুবহু মিলতে হবে
        const string TaskId = "FastDMStartup";

        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "FastDM";

        public const string MinimizedArg = "--minimized";

        public static async Task<StartupState> GetAsync()
        {
            try
            {
                return UpdateChecker.IsPackaged
                    ? await GetPackagedAsync()
                    : GetRegistry();
            }
            catch (Exception ex)
            {
                AppLog.Write("Startup state failed: " + ex.Message);
                return StartupState.Unavailable;
            }
        }

        public static async Task<StartupState> SetAsync(bool enable)
        {
            try
            {
                return UpdateChecker.IsPackaged
                    ? await SetPackagedAsync(enable)
                    : SetRegistry(enable);
            }
            catch (Exception ex)
            {
                AppLog.Write("Startup change failed: " + ex.Message);
                return StartupState.Unavailable;
            }
        }

        // ---------- Store (MSIX) ----------
        static StartupState Map(StartupTaskState s)
        {
            switch (s)
            {
                case StartupTaskState.Enabled:
                case StartupTaskState.EnabledByPolicy:
                    return StartupState.On;
                case StartupTaskState.DisabledByUser:
                    return StartupState.DisabledByUser;
                case StartupTaskState.DisabledByPolicy:
                    return StartupState.DisabledByPolicy;
                default:
                    return StartupState.Off;
            }
        }

        static async Task<StartupState> GetPackagedAsync()
        {
            var task = await StartupTask.GetAsync(TaskId);
            return Map(task.State);
        }

        static async Task<StartupState> SetPackagedAsync(bool enable)
        {
            var task = await StartupTask.GetAsync(TaskId);

            if (enable)
            {
                // DisabledByUser হলে অ্যাপ জোর করে চালু করতে পারে না, ইউজারকে Task Manager থেকে করতে হয়
                if (task.State == StartupTaskState.Disabled)
                    return Map(await task.RequestEnableAsync());

                return Map(task.State);
            }

            if (task.State == StartupTaskState.Enabled)
                task.Disable();

            return Map(task.State);
        }

        // ---------- Portable ----------
        static StartupState GetRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);

            return key?.GetValue(RunName) is string v && v.Length > 0
                ? StartupState.On
                : StartupState.Off;
        }

        static StartupState SetRegistry(bool enable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key == null)
                return StartupState.Unavailable;

            if (enable)
                key.SetValue(RunName, "\"" + Environment.ProcessPath + "\" " + MinimizedArg);
            else
                key.DeleteValue(RunName, false);

            return enable ? StartupState.On : StartupState.Off;
        }

        // স্টার্টআপ টাস্ক দিয়ে চালু হয়েছে কি না (Store বিল্ডে)। Main-এর শুরুতেই ডাকতে হয়।
        // আলাদা মেথড, যাতে পুরোনো Windows-এ API না থাকলে শুধু এই মেথড ব্যর্থ হয়, অ্যাপ নয়।
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool LaunchedByStartupTask()
        {
            try
            {
                var args = AppInstance.GetActivatedEventArgs();

                return args != null &&
                       args.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
            }
            catch
            {
                return false;
            }
        }
    }
}
