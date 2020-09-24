using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DafnyServer {

  [Serializable]
  [DataContract]
  public class DocTag {
    [DataMember]
    public string tagName;
    [DataMember]
    public string tagValue;
  }

  [Serializable]
  [DataContract]
  public class Doc {

    private static Regex LINE_REGEX = new Regex(@"(\s+\*+\s*)?(.*)");

    private static Regex TAG_REGEX = new Regex(@"@([a-zA-Z]*)\s+(.*)");

    public static List<string> SplitParts(string comment) {
      if (comment == null) {
        return new List<string>();
      }
      StringBuilder sb = new StringBuilder();
      List<string> result = new List<string>();
      bool escape = false;
      foreach (char c in comment.Trim()) {
        if (escape) {
          if (c == '@') {
            result.Add(sb.ToString().Trim());
            sb.Clear();
            continue;
          }
          escape = c == '\\';
          sb.Append(c);
        }
      }
      result.Add(sb.ToString().Trim());
      return result;
    }

    public static string ParseBody(string body) {
      StringBuilder sb = new StringBuilder();
      foreach (string rawLine in body.Split('\n')) {
        string line = LINE_REGEX.Match(rawLine).Groups[2].Value.Trim();
        sb.AppendLine(line);
      }
      return sb.ToString().Trim();
    }

    public static DocTag ParseTag(string s) {
      var match = TAG_REGEX.Match(s);
      if (!match.Success) {
        return new DocTag {
          tagName = "",
          tagValue = "(malformed tag)",
        };
      }
      return new DocTag {
        tagName = match.Groups[1].Value.Trim(),
        tagValue = match.Groups[2].Value.Trim(),
      };
    }

    public static Doc Parse(string comment) {
      Console.WriteLine("Comment: " + comment);
      var parts = SplitParts(comment);
      if (parts.Count <= 0) {
        return new Doc {
          body = null,
          tags = new List<DocTag>(),
        };
      }
      var body = parts[0];
      var tags = new List<DocTag>();
      Console.WriteLine("Body: " + body);
      for (int i = 1; i < parts.Count; i++) {
        tags.Add(ParseTag(parts[i]));
      }
      return new Doc {
        body = body,
        tags = tags,
      };
    }

    [DataMember]
    public string body;
    [DataMember]
    public List<DocTag> tags;

  }

}
