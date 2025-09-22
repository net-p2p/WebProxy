using System;
using System.Text;

namespace WebProxy.Extensions
{
    public static class Exp
    {
        public static StringBuilder LogLine(this StringBuilder builder)
        {
            return builder.AppendLine().Append(' ').Append(' ');
        }

        public static StringBuilder LogAppend(this StringBuilder builder, object log)
        {
            return builder.Append(log).Append(' ');
        }

        public static bool IsDocker => Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
    }
}
