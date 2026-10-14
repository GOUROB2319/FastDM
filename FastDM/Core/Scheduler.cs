using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FastDM
{
    public enum PostAction { None, Shutdown, Sleep, Hibernate }

    // ====================== শিডিউলার লজিক ======================
    // ScheduleDays: বিটমাস্ক, Sunday = bit 0 ... Saturday = bit 6
    // সময় মিনিটে (মধ্যরাত থেকে), যেমন 120 = 02:00
    public static class Scheduler
    {
        public static string Hm(int minutes)
        {
            minutes = ((minutes % 1440) + 1440) % 1440;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        // এখন ডাউনলোড চালানোর অনুমতি আছে কি না
        public static bool IsAllowed(AppSettings s, DateTime now)
        {
            int day = (int)now.DayOfWeek;
            int prev = (day + 6) % 7;
            int t = now.Hour * 60 + now.Minute;
            bool today = (s.ScheduleDays & (1 << day)) != 0;
            bool yesterday = (s.ScheduleDays & (1 << prev)) != 0;
            int a = s.ScheduleStartMin, b = s.ScheduleEndMin;

            if (a == b) return today;                         // একই সময় = নির্বাচিত দিনে পুরো দিন
            if (a < b) return today && t >= a && t < b;       // সাধারণ উইন্ডো, যেমন 02:00 -> 06:00
            return (today && t >= a) || (yesterday && t < b); // মধ্যরাত পেরোনো উইন্ডো, যেমন 22:00 -> 06:00
        }

        public static DateTime? NextStart(AppSettings s, DateTime now)
        {
            for (int i = 0; i <= 7; i++)
            {
                var date = now.Date.AddDays(i);
                if ((s.ScheduleDays & (1 << (int)date.DayOfWeek)) == 0) continue;
                var start = date.AddMinutes(s.ScheduleStartMin);
                if (start > now) return start;
            }
            return null;
        }

        public static string Status(AppSettings s, DateTime now, bool allowed)
        {
            if (!s.SchedulerEnabled) return "Schedule: Off";
            if (allowed) return "Schedule: active until " + Hm(s.ScheduleEndMin);
            var next = NextStart(s, now);
            return next == null
                ? "Schedule: no days selected"
                : "Schedule: next start " + next.Value.ToString("ddd HH:mm");
        }
    }

    // ====================== ডাউনলোড শেষে পাওয়ার অ্যাকশন ======================
    public static class PowerActions
    {
        public static string Name(PostAction a)
        {
            switch (a)
            {
                case PostAction.Shutdown: return "shut down";
                case PostAction.Sleep: return "go to sleep";
                case PostAction.Hibernate: return "hibernate";
                default: return "";
            }
        }

        public static void Run(PostAction a)
        {
            try
            {
                switch (a)
                {
                    case PostAction.Shutdown:
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0")
                        { UseShellExecute = false, CreateNoWindow = true });
                        break;
                    case PostAction.Sleep:
                        Application.SetSuspendState(PowerState.Suspend, false, false);
                        break;
                    case PostAction.Hibernate:
                        Application.SetSuspendState(PowerState.Hibernate, false, false);
                        break;
                }
            }
            catch { }
        }
    }
}
