using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DafnyServer {

  public class Doc {

    private static Regex LINE_REGEX = new Regex(@"(\s+\*+\s*)?(.*)");

    public static Doc Parse(string Comment) {
      if (Comment == null) {
        return new Doc {
          Body = null
        };
      }
      Console.WriteLine("Comment: " + Comment);
      StringBuilder sb = new StringBuilder();
      foreach (string rawLine in Comment.Split('\n')) {
        string line = LINE_REGEX.Match(rawLine).Groups[2].Value.Trim();
        sb.AppendLine(line);
      }
      string Body = sb.ToString().Trim();
      Console.WriteLine("Body: " + Body);
      return new Doc {
        Body = Body,
      };
    }

    public string Body;

  }

}
