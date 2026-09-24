using System.Text;

namespace Erp.SharedKernel.Text;

/// <summary>Converts PascalCase enum member names to the snake_case codes the frontend uses (HeadOffice → head_office).</summary>
public static class SnakeCase
{
    public static string From(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var startsWord = i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (startsWord)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
