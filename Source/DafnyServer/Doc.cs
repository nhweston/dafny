using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DafnyServer {

  public class Doc {

    private static Regex LINE_REGEX = new Regex(@"(\s+\*+\s*)?(.*)");

    private static Regex TAG_REGEX = new Regex(@"@([a-zA-Z]*)\s+(\w+)\s+(.*)");

    private static List<string> SplitParts(string comment) {
      if (comment == null) {
        return new List<string>();
      }
      StringBuilder sb = new StringBuilder();
      List<string> result = new List<string>();
      bool escape = false;
      foreach (char c in comment.TrimEnd()) {
        if (!escape && c == '@') {
          result.Add(sb.ToString().Trim());
          sb.Clear();
          continue;
        }
        escape = c == '\\';
        sb.Append(c);
      }
      result.Add(sb.ToString().TrimEnd());
      foreach (var line in result) {
        Console.WriteLine(line);
      }
      return result;
    }

    public Doc(string comment) {
      var parts = SplitParts(comment);
      if (parts.Count <= 0) {
        return;
      }
      ParseBody(parts[0]);
      vparams = new Dictionary<string, string>();
      tparams = new Dictionary<string, string>();
      returns = new Dictionary<string, string>();
      for (int i = 1; i < parts.Count; i++) {
        ParseTag(parts[i]);
      }
    }

    public void ParseBody(string b) {
      StringBuilder sb = new StringBuilder();
      foreach (string rawLine in b.Split('\n')) {
        Console.WriteLine("RAW: " + rawLine);
        string line = LINE_REGEX.Match(rawLine).Groups[2].Value.Trim();
        // Console.WriteLine("LINE: " + line);
        Console.WriteLine("OUT: " + line);
        sb.AppendLine(line);
      }
      body = sb.ToString().Trim();
      // Console.WriteLine(body);
    }

    public void ParseTag(string s) {
      var match = TAG_REGEX.Match(s);
      if (!match.Success) {
        Console.WriteLine("Malformed tag");
        return;
      }
      var kind = match.Groups[1].Value.Trim();
      var name = match.Groups[2].Value.Trim();
      var body = match.Groups[3].Value.Trim();
      if (kind == "param") {
        vparams.Add(name, body);
      }
      else if (kind == "tparam") {
        tparams.Add(name, body);
      }
      else if (kind == "returns") {
        returns.Add(name, body);
      }
      else {
        Console.WriteLine("Unknown tag kind");
      }
    }

    public string body;
    public Dictionary<string, string> vparams;
    public Dictionary<string, string> tparams;
    public Dictionary<string, string> returns;

  }

}
