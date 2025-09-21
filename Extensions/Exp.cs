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
        
    }
}
