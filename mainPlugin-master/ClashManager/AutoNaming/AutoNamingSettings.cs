using System.Collections.Generic;

namespace ClashManager.AutoNaming
{
    public class AutoNamingSettings
    {
        public bool IncludeParam1 { get; set; }
        public string Param1Name { get; set; }

        public bool IncludeParam2 { get; set; }
        public string Param2Name { get; set; }

        public bool IncludeParam3 { get; set; }
        public string Param3Name { get; set; }

        public bool IncludeParam4 { get; set; }
        public string Param4Name { get; set; }

        public bool IncludeParam5 { get; set; }
        public string Param5Name { get; set; }

        private string _separator;
        public string Separator
        {
            get => _separator;
            set
            {
                _separator = value;
                // Ensure separator has spaces around |
                if (!string.IsNullOrEmpty(_separator) && _separator.Contains("|") && !_separator.Contains(" | "))
                {
                    _separator = " | ";
                }
            }
        }

        public AutoNamingSettings()
        {
            Separator = " | ";
        }

        public List<string> GetActiveParameters()
        {
            var parameters = new List<string>();

            if (IncludeParam1 && !string.IsNullOrWhiteSpace(Param1Name))
                parameters.Add(Param1Name);

            if (IncludeParam2 && !string.IsNullOrWhiteSpace(Param2Name))
                parameters.Add(Param2Name);

            if (IncludeParam3 && !string.IsNullOrWhiteSpace(Param3Name))
                parameters.Add(Param3Name);

            if (IncludeParam4 && !string.IsNullOrWhiteSpace(Param4Name))
                parameters.Add(Param4Name);

            if (IncludeParam5 && !string.IsNullOrWhiteSpace(Param5Name))
                parameters.Add(Param5Name);

            return parameters;
        }
    }
}
