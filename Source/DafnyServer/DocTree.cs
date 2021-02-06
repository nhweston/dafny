using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Dafny;
using Function = Microsoft.Dafny.Function;
using Program = Microsoft.Dafny.Program;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using IToken = Microsoft.Boogie.IToken;

namespace DafnyServer {

  public class DocTree {

    private static Regex LINK_REGEX_0 =
      new Regex(@"\[(?:[^\]]|\\])*]\((.*)\)", RegexOptions.Singleline);
    private static Regex LINK_REGEX_1 =
      new Regex(@"\[(?:[^\]]|\\])*]:\s+([^\s]+)", RegexOptions.Singleline);
    private static Regex INTERNAL_LINK_REGEX = new Regex(@"[\w-'?.]+");

    private static string ConvertToJson<T>(T data) {
      var serializer = new DataContractJsonSerializer(typeof(T));
      using (var ms = new MemoryStream()) {
        serializer.WriteObject(ms, data);
        return Encoding.Default.GetString(ms.ToArray());
      }
    }

    private readonly Program _program;

    public DocTree(Program program) {
      _program = program;
    }

    public ICollection<FileNode> GetDocTree() {
      var module = _program.DefaultModuleDef;
      return CollectFiles(module);
    }

    private ICollection<FileNode> CollectFiles(ModuleDefinition module) {
      // collect all top-level declarations
      var allDecls = CollectDecls(module);
      // collect includes
      var includesByFile = new Dictionary<string, List<string>>();
      foreach (var include in module.Includes) {
        var includer = include.includerFilename;
        if (!includesByFile.ContainsKey(includer)) {
          includesByFile.Add(includer, new List<string>());
        }
        includesByFile[includer].Add(include.includedFilename);
      }
      // associate files to declarations
      var files = new Dictionary<string, FileNode>();
      foreach (var decl in allDecls) {
        var filename = decl.tok.filename;
        if (!files.ContainsKey(filename)) {
          var includes = new List<ICollection<string>>();
          foreach (var path in includesByFile.GetValueOrDefault(filename, new List<string>())) {
            var include = path.Split(Path.DirectorySeparatorChar);
            includes.Add(include);
          }
          files[filename] = new FileNode {
            Path = filename,
            Decls = new List<DeclNode>(),
            Includes = includes,
          };
        }
        files[filename].Decls.Add(ToDeclNode(decl));
      }
      return files.Values;
    }

    private ICollection<Declaration> CollectDecls(ModuleDefinition module) {
      var result = new List<Declaration>();
      ClassDecl defaultClass = null;
      foreach (var decl in module.TopLevelDecls) {
        if (decl is ClassDecl cl && cl.IsDefaultClass) {
          defaultClass = cl;
          continue;
        }
        result.Add(decl);
      }
      foreach (var member in defaultClass.Members) {
        result.Add(member);
      }
      return result;
    }

    private DeclNode ToDeclNode(Declaration decl) {
      if (decl is AliasModuleDecl am) {
        return ToImportNode(am);
      }
      if (decl is LiteralModuleDecl mod) {
        return ToModuleNode(mod.ModuleDef, new DocComment(mod.DocComment));
      }
      if (decl is ClassDecl cl) {
        return ToClassNode(cl);
      }
      if (decl is DatatypeDecl dt) {
        return ToDatatypeNode(dt);
      }
      if (decl is OpaqueTypeDecl ot) {
        return ToTypeSynonymNode(ot);
      }
      if (decl is NewtypeDecl nt) {
        return ToNewtypeNode(nt);
      }
      if (decl is Function f) {
        return ToFunctionNode(f);
      }
      if (decl is Method m) {
        return ToMethodNode(m);
      }
      if (decl is Field field) {
        return ToFieldNode(field);
      }
      Console.WriteLine("Unknown declaration type: " + decl.GetType().Name);
      return null;
    }

    private ImportNode ToImportNode(AliasModuleDecl am) {
      Console.WriteLine("Import: " + am.Name);
      return new ImportNode {
        Name = am.Name,
        Target = ToTokenNode(am.TargetQId.Def.tok),
        IsOpened = am.Opened,
        Token = ToTokenNode(am.tok),
      };
    }

    private ModuleNode ToModuleNode(ModuleDefinition mod, DocComment dc) {
      Console.WriteLine("Module: " + mod.Name);
      return new ModuleNode {
        Name = mod.Name,
        IsAbstract = mod.IsAbstract,
        Refines = mod.RefinementQId == null ? null : mod.RefinementQId.ToString(),
        Decls = CollectDecls(mod).Select(ToDeclNode).Where(d => d != null).ToList(),
        UserDoc = dc == null ? null : dc.MainBody,
        Token = ToTokenNode(mod.tok),
      };
    }

    private ClassNode ToClassNode(ClassDecl cl) {
      Console.WriteLine("Class: " + cl.Name);
      var dc = new DocComment(cl.DocComment);
      return new ClassNode {
        Name = cl.Name,
        IsTrait = cl is TraitDecl,
        TypeParams = cl.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        Extends = cl.ParentTraits.Select(t => t.ToString()).ToList(),
        Members = cl.Members.Select(ToDeclNode).ToList(),
        UserDoc = dc.MainBody,
        Token = ToTokenNode(cl.tok),
      };
    }

    private DatatypeNode ToDatatypeNode(DatatypeDecl dt) {
      Console.WriteLine("Datatype: " + dt.Name);
      var dc = new DocComment(dt.DocComment);
      var ctors = new List<CtorNode>();
      foreach (var ctor in dt.Ctors) {
        Console.WriteLine("Constructor: " + ctor.Name);
        ctors.Add(new CtorNode {
          Name = ctor.Name,
          ValueParams = ctor.Formals.Select(f => ToValueParamNode(f, null, false)).ToList(),
          Token = ToTokenNode(ctor.tok),
        });
      }
      return new DatatypeNode {
        Name = dt.Name,
        IsCodata = dt is CoDatatypeDecl,
        TypeParams = dt.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        Ctors = ctors,
        UserDoc = dc.MainBody,
      };
    }

    private TypeSynonymNode ToTypeSynonymNode(TypeSynonymDecl ts) {
      Console.WriteLine("Type synonym: " + ts.Name);
      var dc = new DocComment(ts.DocComment);
      return new TypeSynonymNode {
        Name = ts.Name,
        TypeParams = ts.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        Rhs = ts.Rhs.ToString(),
        UserDoc = dc.MainBody,
        Token = ToTokenNode(ts.tok),
      };
    }

    private TypeSynonymNode ToTypeSynonymNode(OpaqueTypeDecl ot) {
      Console.WriteLine("Type synonym: " + ot.Name);
      var dc = new DocComment(ot.DocComment);
      return new TypeSynonymNode {
        Name = ot.Name,
        TypeParams = ot.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        Rhs = null,
        UserDoc = dc.MainBody,
        Token = ToTokenNode(ot.tok),
      };
    }

    private NewtypeNode ToNewtypeNode(NewtypeDecl nt) {
      Console.WriteLine("Newtype: " + nt.Name);
      var dc = new DocComment(nt.DocComment);
      var token = ToTokenNode(nt.tok);
      return new NewtypeNode {
        Name = nt.Name,
        BaseType = ToTypeRefNode(nt.BaseType, token),
        Constraint = Printer.ExprToString(nt.Constraint),
        UserDoc = dc.MainBody,
        Token = ToTokenNode(nt.tok),
      };
    }

    private FunctionNode ToFunctionNode(Function f) {
      Console.WriteLine("Function: " + f.Name);
      var dc = new DocComment(f.DocComment);
      var modifiers = new List<string>();
      if (f.IsGhost) {
        modifiers.Add("ghost");
      }
      if (f.IsStatic) {
        modifiers.Add("static");
      }
      var token = ToTokenNode(f.tok);
      var spec = new List<SpecNode>();
      foreach (var req in f.Req) {
        spec.Add(ToSpecNode(req, "requires", token));
      }
      foreach (var ens in f.Ens) {
        spec.Add(ToSpecNode(ens, "ensures", token));
      }
      foreach (var reads in f.Reads) {
        spec.Add(ToSpecNode(reads, "reads", token));
      }
      spec.Add(ToSpecNode(f.Decreases, "decreases", token));
      return new FunctionNode {
        Name = f.Name,
        Kind =
          f is GreatestPredicate ? "greatest predicate"
          : f is LeastPredicate ? "least predicate"
          : f is Predicate || f is TwoStatePredicate ?
            f.IsGhost ? "predicate"
            : "predicate method"
          : f.IsGhost ? "function"
          : "function method",
        Modifiers = modifiers,
        TypeParams = f.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        ValueParams = f.Formals.Select(vp => ToValueParamNode(vp, dc, false)).ToList(),
        ReturnType = ToTypeRefNode(f.ResultType, token),
        Spec = spec,
        UserDoc = dc.MainBody,
        Token = token,
      };
    }

    private MethodNode ToMethodNode(Method m) {
      Console.WriteLine("Method: " + m.Name);
      var dc = new DocComment(m.DocComment);
      var modifiers = new List<string>();
      if (m.IsGhost) {
        modifiers.Add("ghost");
      }
      if (m.IsStatic) {
        modifiers.Add("static");
      }
      var token = ToTokenNode(m.tok);
      var spec = new List<SpecNode>();
      foreach (var req in m.Req) {
        spec.Add(ToSpecNode(req, "requires", token));
      }
      foreach (var ens in m.Ens) {
        spec.Add(ToSpecNode(ens, "ensures", token));
      }
      spec.Add(ToSpecNode(m.Mod, "modifies", token));
      spec.Add(ToSpecNode(m.Decreases, "decreases", token));
      return new MethodNode {
        Name = m.Name,
        Kind =
          m is Constructor ? "constructor"
          : m is GreatestLemma ? "greatest lemma"
          : m is LeastLemma ? "least lemma"
          : m is TwoStateLemma ? "twostate lemma"
          : m is Lemma ? "lemma"
          : "method",
        Modifiers = modifiers,
        TypeParams = m.TypeArgs.Select(tp => ToTypeParamNode(tp, dc)).ToList(),
        ValueParams = m.Ins.Select(vp => ToValueParamNode(vp, dc, false)).ToList(),
        Returns = m.Outs.Select(vp => ToValueParamNode(vp, dc, true)).ToList(),
        Spec = spec,
        UserDoc = dc.MainBody,
        Token = token,
      };
    }

    private FieldNode ToFieldNode(Field f) {
      Console.WriteLine("Field: " + f.Name);
      var dc = new DocComment(f.DocComment);
      var token = ToTokenNode(f.tok);
      return new FieldNode {
        Name = f.Name,
        Type = ToTypeRefNode(f.Type, token),
        UserDoc = dc.MainBody,
        Token = token,
      };
    }

    private TokenNode ToTokenNode(IToken tok) {
      var pathRaw = tok.filename.Split('[')[0];
      return new TokenNode {
        Path = pathRaw.Split(Path.DirectorySeparatorChar),
        Line = tok.line,
        Column = tok.col,
      };
    }

    private TypeParamNode ToTypeParamNode(TypeParameter tp, DocComment dc) {
      Console.WriteLine("Type parameter: " + tp.Name);
      return new TypeParamNode {
        Name = tp.Name,
        UserDoc = dc == null || dc.TypeParamTags == null ? null : dc.TypeParamTags[tp.Name],
        Token = ToTokenNode(tp.tok),
      };
    }

    private ValueParamNode ToValueParamNode(Formal f, DocComment dc, bool isOut) {
      Console.WriteLine("Value parameter: " + f.Name);
      string userDoc = null;
      if (dc != null) {
        var tags = isOut ? dc.ReturnTags : dc.ValueParamTags;
        if (tags != null) {
          userDoc = tags.GetValueOrDefault(f.Name);
        }
      }
      var token = ToTokenNode(f.tok);
      return new ValueParamNode {
        Name = f.Name,
        Type = ToTypeRefNode(f.Type, token),
        UserDoc = userDoc,
        Token = token,
      };
    }

    private SpecNode ToSpecNode(AttributedExpression expr, string kind, TokenNode token) {
      Console.WriteLine("Specification (" + kind + ")");
      var dc = new DocComment(expr.DocComment);
      return new SpecNode {
        Kind = kind,
        Clause = Printer.ExprToString(expr.E),
        UserDoc = dc.MainBody,
        Token = token,
      };
    }

    private SpecNode ToSpecNode(FrameExpression expr, string kind, TokenNode token) {
      Console.WriteLine("Specification (" + kind + ")");
      return new SpecNode {
        Kind = kind,
        Clause = Printer.ExprToString(expr.E),
        UserDoc = null,
        Token = token,
      };
    }

    private SpecNode ToSpecNode(Specification<Expression> spec, string kind, TokenNode token) {
      Console.WriteLine("Specification (" + kind + ")");
      return new SpecNode {
        Kind = kind,
        Clause = string.Join(", ", spec.Expressions.Select(Printer.ExprToString)),
        UserDoc = null,
        Token = token,
      };
    }

    private SpecNode ToSpecNode(Specification<FrameExpression> spec, string kind, TokenNode token) {
      Console.WriteLine("Specification (" + kind + ")");
      return new SpecNode {
        Kind = kind,
        Clause = string.Join(", ", spec.Expressions.Select(e => Printer.ExprToString(e.E))),
        UserDoc = null,
        Token = token,
      };
    }

    private TypeRefNode ToTypeRefNode(Microsoft.Dafny.Type t, TokenNode token) {
      if (t.IsRevealableType && t.AsRevealableType is TypeSynonymDecl ts && ts.tok != null &&
          ts.tok.filename != null) {
        Console.WriteLine("Type synonym reference: " + ts.Name);
        return new TypeRefNode {
          Name = ts.Name,
          Target = ToTokenNode(ts.tok),
          TypeParams = t.TypeArgs.Select(tp => ToTypeRefNode(tp, token)).ToList(),
          Token = token,
        };
      }
      if (t.IsArrayType) {
        Console.WriteLine("Array type reference");
        var at = t.AsArrayType;
        var tps = new List<TypeRefNode>();
        tps.Add(ToTypeRefNode(t.TypeArgs[0], token));
        return new TypeRefNode {
          Name = "array",
          TypeParams = tps,
          Token = token,
        };
      }
      if (t.AsCollectionType != null) {
        Console.WriteLine("Collection type reference");
        var ct = t.AsCollectionType;
        return new TypeRefNode {
          Name = ct.CollectionTypeName,
          TypeParams = t.TypeArgs.Select(tp => ToTypeRefNode(tp, token)).ToList(),
          Token = token,
        };
      }
      if (t.IsArrowType) {
        Console.WriteLine("Function type reference");
        var at = t.AsArrowType;
        return new TypeRefNode {
          Name = at.Name,
          TypeParams = t.TypeArgs.Select(tp => ToTypeRefNode(tp, token)).ToList(),
          Special = "Function",
          Token = token,
        };
      }
      if (t.IsTypeParameter) {
        Console.WriteLine("Type parameter reference");
        var tp = t.AsTypeParameter;
        return new TypeRefNode {
          Name = tp.Name,
          Target = ToTokenNode(tp.tok),
          TypeParams = new TypeRefNode[0],
          Token = token,
        };
      }
      if (t.IsDatatype) {
        Console.WriteLine("Tuple type reference");
        var dt = t.AsDatatype;
        if (dt is TupleTypeDecl tt) {
          return new TypeRefNode {
            Name = dt.Name,
            TypeParams = t.TypeArgs.Select(tp => ToTypeRefNode(tp, token)).ToList(),
            Special = "Tuple",
            Token = token,
          };
        }
      }
      if (t.IsNonNullRefType) {
        var udt = t.AsNonNullRefType;
        Console.WriteLine("Type reference: " + udt.Name);
        return new TypeRefNode {
          Name = udt.Name,
          Target = ToTokenNode(udt.tok),
          TypeParams = t.TypeArgs.Select(tp => ToTypeRefNode(tp, token)).ToList(),
          Token = token,
        };
      }
      Console.WriteLine("Other type reference: " + t.ToString());
      return new TypeRefNode {
        Name = t.ToString(),
        TypeParams = new TypeRefNode[0],
        Token = token,
      };
    }

  }

  public class DocComment {

    private static Regex LINE_REGEX = new Regex(@"(\s+\*+\s*)?(.*)");
    private static Regex TAG_REGEX = new Regex(@"@([a-zA-Z]*)\s+(\w+)\s+(.*)");

    private static List<string> SplitParts(string comment) {
      if (comment == null) {
        return new List<string>();
      }
      StringBuilder buffer = new StringBuilder();
      List<string> result = new List<string>();
      bool escape = false;
      foreach (char c in comment.TrimEnd()) {
        if (!escape && c == '@') {
          result.Add(buffer.ToString().Trim());
          buffer.Clear();
          continue;
        }
        escape = !escape && c == '\\';
        buffer.Append(c);
      }
      result.Add(buffer.ToString().Trim());
      return result;
    }

    // TODO: Removal of asterisks should be in `SplitParts` phase
    private static string ParseBody(string text) {
      StringBuilder buffer = new StringBuilder();
      foreach (string rawLine in text.Split('\n')) {
        string line = LINE_REGEX.Match(rawLine).Groups[2].Value.Trim();
        buffer.AppendLine(line);
      }
      return buffer.ToString().Trim();
    }

    public string MainBody;
    public Dictionary<string, string> ValueParamTags = new Dictionary<string, string>();
    public Dictionary<string, string> TypeParamTags = new Dictionary<string, string>();
    public Dictionary<string, string> ReturnTags = new Dictionary<string, string>();

    public DocComment(string comment) {
      var parts = SplitParts(comment);
      if (!parts.Any()) {
        return;
      }
      MainBody = ParseBody(parts[0]);
      for (int i = 1; i < parts.Count; i++) {

      }
    }

    public void ParseTag(string text) {
      var match = TAG_REGEX.Match(text);
      if (!match.Success) {
        Console.WriteLine("Malformed tag");
        return;
      }
      var groups = match.Groups;
      var kind = groups[1].Value.Trim();
      var name = groups[2].Value.Trim();
      var body = ParseBody(groups[3].Value.Trim());
      switch (kind) {
        case "param":
          ValueParamTags.Add(name, body);
          break;
        case "tparam":
          TypeParamTags.Add(name, body);
          break;
        case "return":
          ReturnTags.Add(name, body);
          break;
        default:
          Console.WriteLine("Unknown tag kind");
          break;
      }
    }

  }

  [Serializable]
  [DataContract]
  public class DeclNode { }

  [Serializable]
  [DataContract]
  public class SpecNode {
    [DataMember]
    public string Kind;
    [DataMember]
    public string Clause;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class ValueParamNode {
    [DataMember]
    public string Name;
    [DataMember]
    public TypeRefNode Type;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class TypeParamNode {
    [DataMember]
    public string Name;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class CtorNode {
    [DataMember]
    public string Name;
    [DataMember]
    public ICollection<ValueParamNode> ValueParams;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class TypeRefNode {
    [DataMember]
    public string Name;
    [DataMember]
    public TokenNode Target;
    [DataMember]
    public ICollection<TypeRefNode> TypeParams;
    [DataMember]
    public string Special;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class TokenNode {
    [DataMember]
    public ICollection<string> Path;
    [DataMember]
    public int Line;
    [DataMember]
    public int Column;
  }

  [Serializable]
  [DataContract]
  [KnownType(typeof(ModuleNode))]
  [KnownType(typeof(ImportNode))]
  [KnownType(typeof(ClassNode))]
  [KnownType(typeof(DatatypeNode))]
  [KnownType(typeof(TypeSynonymNode))]
  [KnownType(typeof(NewtypeNode))]
  [KnownType(typeof(FunctionNode))]
  [KnownType(typeof(MethodNode))]
  [KnownType(typeof(FieldNode))]
  public class FileNode: DeclNode {
    [DataMember]
    public string Path;
    [DataMember]
    public ICollection<DeclNode> Decls;
    [DataMember]
    public ICollection<ICollection<string>> Includes;
  }

  [Serializable]
  [DataContract]
  [KnownType(typeof(ModuleNode))]
  [KnownType(typeof(ImportNode))]
  [KnownType(typeof(ClassNode))]
  [KnownType(typeof(DatatypeNode))]
  [KnownType(typeof(TypeSynonymNode))]
  [KnownType(typeof(NewtypeNode))]
  [KnownType(typeof(FunctionNode))]
  [KnownType(typeof(MethodNode))]
  [KnownType(typeof(FieldNode))]
  public class ModuleNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public bool IsAbstract;
    [DataMember]
    public string Refines;
    [DataMember]
    public ICollection<DeclNode> Decls;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
    [DataMember]
    public ICollection<ImportNode> Imports;
  }

  [Serializable]
  [DataContract]
  public class ImportNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public TokenNode Target;
    [DataMember]
    public bool IsOpened;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  [KnownType(typeof(FunctionNode))]
  [KnownType(typeof(MethodNode))]
  [KnownType(typeof(FieldNode))]
  public class ClassNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public bool IsTrait;
    [DataMember]
    public ICollection<TypeParamNode> TypeParams;
    [DataMember]
    public ICollection<string> Extends;
    [DataMember]
    public ICollection<DeclNode> Members;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class DatatypeNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public bool IsCodata;
    [DataMember]
    public ICollection<TypeParamNode> TypeParams;
    [DataMember]
    public ICollection<CtorNode> Ctors;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class TypeSynonymNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public ICollection<TypeParamNode> TypeParams;
    [DataMember]
    public string Rhs;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class NewtypeNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public TypeRefNode BaseType;
    [DataMember]
    public string Constraint;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class FunctionNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public string Kind;
    [DataMember]
    public ICollection<string> Modifiers;
    [DataMember]
    public ICollection<TypeParamNode> TypeParams;
    [DataMember]
    public ICollection<ValueParamNode> ValueParams;
    [DataMember]
    public TypeRefNode ReturnType;
    [DataMember]
    public ICollection<SpecNode> Spec;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class MethodNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public string Kind;
    [DataMember]
    public ICollection<string> Modifiers;
    [DataMember]
    public ICollection<TypeParamNode> TypeParams;
    [DataMember]
    public ICollection<ValueParamNode> ValueParams;
    [DataMember]
    public ICollection<ValueParamNode> Returns;
    [DataMember]
    public ICollection<SpecNode> Spec;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }

  [Serializable]
  [DataContract]
  public class FieldNode: DeclNode {
    [DataMember]
    public string Name;
    [DataMember]
    public TypeRefNode Type;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }


}
