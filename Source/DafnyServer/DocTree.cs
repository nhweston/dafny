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
          files[filename] = new FileNode {
            Path = filename,
            Decls = new List<DeclNode>(),
            Includes = includesByFile.GetValueOrDefault(filename, new List<string>()),
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
      return new ImportNode {
        Name = am.Name,
        Target = ToTokenNode(am.TargetQId.Def.tok),
        IsOpened = am.Opened,
        Token = ToTokenNode(am.tok),
      };
    }

    private ModuleNode ToModuleNode(ModuleDefinition mod, DocComment dc) {
      return new ModuleNode {
        Name = mod.Name,
        IsAbstract = mod.IsAbstract,
        Refines = mod.RefinementQId.ToString(),
        Decls = CollectDecls(mod).Select(ToDeclNode).Where(d => d != null).ToList(),
        UserDoc = dc == null ? null : dc.MainBody,
        Token = ToTokenNode(mod.tok),
      };
    }

    private ClassNode ToClassNode(ClassDecl cl) {
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
      var dc = new DocComment(dt.DocComment);
      var ctors = new List<CtorNode>();
      foreach (var ctor in dt.Ctors) {
        ctors.Add(new CtorNode {
          Name = ctor.Name,
          ValueParams = ctor.Formals.Select(f => ToFormalNode(f, null, false)).ToList(),
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
      var dc = new DocComment(nt.DocComment);
      return new NewtypeNode {
        Name = nt.Name,
        BaseType = nt.BaseType.ToString(),
        Constraint = Printer.ExprToString(nt.Constraint),
        UserDoc = dc.MainBody,
        Token = ToTokenNode(nt.tok),
      };
    }

    private FunctionNode ToFunctionNode(Function f) {
      var dc = new DocComment(f.DocComment);
      var modifiers = new List<string>();
      if (f.IsGhost) {
        modifiers.Add("ghost");
      }
      if (f.IsStatic) {
        modifiers.Add("static");
      }
      var spec = new List<SpecNode>();
      foreach (var req in f.Req) {
        spec.Add(ToSpecNode(req, "requires"));
      }
      foreach (var ens in f.Ens) {
        spec.Add(ToSpecNode(ens, "ensures"));
      }
      foreach (var reads in f.Reads) {
        spec.Add(ToSpecNode(reads, "reads"));
      }
      spec.Add(ToSpecNode(f.Decreases, "decreases"));
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
        ValueParams = f.Formals.Select(vp => ToFormalNode(vp, dc, false)).ToList(),
        ReturnType = f.ResultType.ToString(),
        Spec = spec,
        UserDoc = dc.MainBody,
        Token = ToTokenNode(f.tok),
      };
    }

    private MethodNode ToMethodNode(Method m) {
      var dc = new DocComment(m.DocComment);
      var modifiers = new List<string>();
      if (m.IsGhost) {
        modifiers.Add("ghost");
      }
      if (m.IsStatic) {
        modifiers.Add("static");
      }
      var spec = new List<SpecNode>();
      foreach (var req in m.Req) {
        spec.Add(ToSpecNode(req, "requires"));
      }
      foreach (var ens in m.Ens) {
        spec.Add(ToSpecNode(ens, "ensures"));
      }
      spec.Add(ToSpecNode(m.Mod, "modifies"));
      spec.Add(ToSpecNode(m.Decreases, "decreases"));
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
        ValueParams = m.Ins.Select(vp => ToFormalNode(vp, dc, false)).ToList(),
        Returns = m.Outs.Select(vp => ToFormalNode(vp, dc, true)).ToList(),
        Spec = spec,
        UserDoc = dc.MainBody,
        Token = ToTokenNode(m.tok),
      };
    }

    private FieldNode ToFieldNode(Field f) {
      var dc = new DocComment(f.DocComment);
      return new FieldNode {
        Name = f.Name,
        Type = f.Type.ToString(),
        UserDoc = dc.MainBody,
        Token = ToTokenNode(f.tok),
      };
    }

    private TokenNode ToTokenNode(IToken tok) {
      return new TokenNode {
        File = tok.filename.Split('[')[0],
        Line = tok.line,
        Column = tok.col,
      };
    }

    private TypeParamNode ToTypeParamNode(TypeParameter tp, DocComment dc) {
      return new TypeParamNode {
        Name = tp.Name,
        UserDoc = dc == null || dc.TypeParamTags == null ? null : dc.TypeParamTags[tp.Name],
        Token = ToTokenNode(tp.tok),
      };
    }

    private FormalNode ToFormalNode(Formal f, DocComment dc, bool isOut) {
      string userDoc = null;
      if (dc != null) {
        var tags = isOut ? dc.ReturnTags : dc.ValueParamTags;
        if (tags != null) {
          userDoc = tags.GetValueOrDefault(f.Name);
        }
      }
      return new FormalNode {
        Name = f.Name,
        Type = ToTypeRefNode(f.Type),
        UserDoc = userDoc,
        Token = ToTokenNode(f.tok),
      };
    }

    private SpecNode ToSpecNode(AttributedExpression expr, string kind) {
      var dc = new DocComment(expr.DocComment);
      return new SpecNode {
        Kind = kind,
        Clause = Printer.ExprToString(expr.E),
        UserDoc = dc.MainBody,
      };
    }

    private SpecNode ToSpecNode(FrameExpression expr, string kind) {
      return new SpecNode {
        Kind = kind,
        Clause = Printer.ExprToString(expr.E),
        UserDoc = null,
      };
    }

    private SpecNode ToSpecNode(Specification<Expression> spec, string kind) {
      return new SpecNode {
        Kind = kind,
        Clause = string.Join(", ", spec.Expressions.Select(Printer.ExprToString)),
        UserDoc = null,
      };
    }

    private SpecNode ToSpecNode(Specification<FrameExpression> spec, string kind) {
      return new SpecNode {
        Kind = kind,
        Clause = string.Join(", ", spec.Expressions.Select(e => Printer.ExprToString(e.E))),
        UserDoc = null,
      };
    }

    private TypeRefNode ToTypeRefNode(Microsoft.Dafny.Type t) {
      if (t.IsRevealableType && t.AsRevealableType is TypeSynonymDecl ts && ts.tok != null &&
          ts.tok.filename != null) {
        return new TypeRefNode {
          Name = ts.Name,
          Target = ToTokenNode(ts.tok),
          TypeParams = t.TypeArgs.Select(ToTypeRefNode).ToList(),
        };
      }
      if (t.IsArrayType) {
        var at = t.AsArrayType;
        var tps = new List<TypeRefNode>();
        tps.Add(ToTypeRefNode(t.TypeArgs[0]));
        return new TypeRefNode {
          Name = "array",
          TypeParams = tps,
        };
      }
      if (t.AsCollectionType != null) {
        var ct = t.AsCollectionType;
        return new TypeRefNode {
          Name = ct.CollectionTypeName,
          TypeParams = t.TypeArgs.Select(ToTypeRefNode).ToList(),
        };
      }
      if (t.IsArrowType) {
        var at = t.AsArrowType;
        return new TypeRefNode {
          Name = at.Name,
          TypeParams = t.TypeArgs.Select(ToTypeRefNode).ToList(),
          Special = "Function",
        };
      }
      if (t.IsTypeParameter) {
        var tp = t.AsTypeParameter;
        return new TypeRefNode {
          Name = tp.Name,
          Target = ToTokenNode(tp.tok),
          TypeParams = new TypeRefNode[0],
        };
      }
      if (t.IsDatatype) {
        var dt = t.AsDatatype;
        if (dt is TupleTypeDecl tt) {
          return new TypeRefNode {
            Name = dt.Name,
            TypeParams = t.TypeArgs.Select(ToTypeRefNode).ToList(),
            Special = "Tuple",
          };
        }
      }
      if (t.IsNonNullRefType) {
        var udt = t.AsNonNullRefType;
        return new TypeRefNode {
          Name = udt.Name,
          Target = ToTokenNode(udt.tok),
          TypeParams = t.TypeArgs.Select(ToTypeRefNode).ToList(),
        };
      }
      return new TypeRefNode {
        Name = t.ToString(),
        TypeParams = new TypeRefNode[0],
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
  }

  [Serializable]
  [DataContract]
  public class FormalNode {
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
    public ICollection<FormalNode> ValueParams;
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
    public string File;
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
    public ICollection<string> Includes;
    [DataMember]
    public ICollection<ImportNode> Imports;
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
    public string BaseType;
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
    public ICollection<FormalNode> ValueParams;
    [DataMember]
    public string ReturnType;
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
    public ICollection<FormalNode> ValueParams;
    [DataMember]
    public ICollection<FormalNode> Returns;
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
    public string Type;
    [DataMember]
    public string UserDoc;
    [DataMember]
    public TokenNode Token;
  }


}
