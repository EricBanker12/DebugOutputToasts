using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DebugOutputToasts
{
    public class Configuration
    {
        public bool ShowNotifications { get; set; } = true;
        public bool PlaySound { get; set; } = false;
        public bool Debounce { get; set; } = false;
        public int DebounceTime { get; set; } = 300;
        public bool Throttle { get; set; } = true;
        public int ThrottleTime { get; set; } = 5000;
        public int MaxDebugMessageHistory { get; set; } = 2000;
        public Filter[] InclusionFilters { get; set; } = new Filter[] { new Filter() };
        public Filter[] ExclusionFilters { get; set; } = new Filter[] { new Filter() };
        public ReplacementFilter[] ReplacementFilters { get; set; } = new ReplacementFilter[] { new ReplacementFilter() };
        public bool MinimizeToTrayIcon { get; set; } = false;
    }

    public class Filter
    {
        public string Find { get; set; } = string.Empty;
        public bool IsMatchCase { get; set; } = false;
        public bool IsUseRegex { get; set; } = false;
    }

    public class ReplacementFilter : Filter
    {
        public string Replace { get; set; } = string.Empty;
    }
}
