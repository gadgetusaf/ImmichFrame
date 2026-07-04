
namespace ImmichFrame.Core.Helpers
{
    public static class DotEnv
    {
        public static void Load(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            foreach (var line in File.ReadAllLines(filePath))
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
                    continue;

                var index = trimmedLine.IndexOf('=');

                if (index == -1)
                    continue;

                var variable = trimmedLine.Substring(0, index).Trim();
                var value = trimmedLine.Substring(index + 1).Trim();

                if (variable.Length == 0)
                    continue;

                if (value.Length >= 2
                    && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                Environment.SetEnvironmentVariable(variable, value);
            }
        }
    }
}
