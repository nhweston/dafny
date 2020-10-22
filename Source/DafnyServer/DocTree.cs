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

    private static Regex LINK_REGEX_0 = new Regex(@"\[(?:[^\]]|\\])*]\((.*)\)");

    private static Regex LINK_REGEX_1 = new Regex(@"\[(?:[^\]]|\\])*]:\s+([^\s]+)");

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

    public ICollection<FileInfo> GetDocTree() {
      Console.WriteLine("Constructing doc tree");
      var mod = _program.DefaultModuleDef;
      var files = new Dictionary<string, FileInfo>();
      return this.CollectTopLevelDecls(mod);
    }

    private ICollection<FileInfo> CollectTopLevelDecls(ModuleDefinition mod) {
      // collect all top-level declarations
      var all = new List<Declaration>();
      ClassDecl dfcl = null;
      foreach (var decl in mod.TopLevelDecls) {
        if (decl is ClassDecl cl && cl.IsDefaultClass) {
          dfcl = cl;
          continue;
        }
        all.Add(decl);
      }
      foreach (var member in dfcl.Members) {
        all.Add(member);
      }
      // collect includes
      var includesByFile = new Dictionary<string, List<string>>();
      foreach (var incl in mod.Includes) {
        if (!includesByFile.ContainsKey(incl.includerFilename)) {
          includesByFile.Add(incl.includerFilename, new List<string>());
        }
        includesByFile[incl.includerFilename].Add(incl.includedFilename);
      }
      // associate files to declarations
      var files = new Dictionary<string, FileInfo>();
      foreach (var decl in all) {
        var fname = decl.tok.filename;
        if (!files.ContainsKey(fname)) {
          files[fname] = new FileInfo {
            path = fname,
            decls = new List<DeclInfo>(),
            includes = includesByFile.GetValueOrDefault(fname, new List<string>()),
          };
        }
        files[fname].decls.Add(GetDeclInfo(decl));
      }
      return files.Values;
    }

    private ICollection<Declaration> CollectDecls(ModuleDefinition mod) {
      var result = new List<Declaration>();
      ClassDecl dfcl = null;
      foreach (var decl in mod.TopLevelDecls) {
        if (decl is ClassDecl cl && cl.IsDefaultClass) {
          dfcl = cl;
          continue;
        }
        result.Add(decl);
      }
      foreach (var member in dfcl.Members) {
        result.Add(member);
      }
      return result;
    }

    private DeclInfo GetDeclInfo(Declaration decl) {
      if (decl is AliasModuleDecl am) return GetImportInfo(am);
      if (decl is LiteralModuleDecl mod) return GetModuleInfo(mod.ModuleDef, mod.DocComment);
      if (decl is ClassDecl cl) return GetClassInfo(cl);
      if (decl is DatatypeDecl dt) return GetDatatypeInfo(dt);
      if (decl is TypeSynonymDecl ts) return GetTypeSynonymInfo(ts);
      if (decl is OpaqueTypeDecl ot) return GetTypeSynonymInfo(ot);
      if (decl is NewtypeDecl nt) return GetNewtypeInfo(nt);
      if (decl is Function f) return GetFunctionInfo(f);
      if (decl is Method m) return GetMethodInfo(m);
      if (decl is Field fld) return GetFieldInfo(fld);
      Console.WriteLine("Unknown declaration type: " + decl.GetType().Name);
      return null;
    }

    private SpecInfo GetSpecInfo(MaybeFreeExpression exp, string kind) {
      Console.WriteLine("Specification clause (MaybeFreeExpression)");
      var r = new SpecInfo {
        kind = kind,
        clause = Printer.ExprToString(exp.E),
        doc = (new Doc(exp.DocComment)).body,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private SpecInfo GetSpecInfo(FrameExpression exp, string kind) {
      Console.WriteLine("Specification clause (FrameExpression)");
      var r = new SpecInfo {
        kind = kind,
        clause = Printer.ExprToString(exp.E),
        doc = null,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private SpecInfo GetSpecInfo(Specification<Expression> se, string kind) {
      Console.WriteLine("Specification clause (Specification<Expression>)");
      var r = new SpecInfo {
        kind = kind,
        clause = string.Join(", ", se.Expressions.Select(e => Printer.ExprToString(e))),
        doc = null,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private SpecInfo GetSpecInfo(Specification<FrameExpression> se, string kind) {
      Console.WriteLine("Specification clause (Specification<FrameExpression>");
      var r = new SpecInfo {
        kind = kind,
        clause = string.Join(", ", se.Expressions.Select(e => Printer.ExprToString(e.E))),
        doc = null,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private FormalInfo GetFormalInfo(Formal f, Doc doc, bool isOut) {
      Console.WriteLine("Formal: " + f.Name);
      string fdoc = null;
      if (doc != null) {
        var dict = isOut ? doc.returns : doc.vparams;
        if (dict != null) {
          fdoc = dict.GetValueOrDefault(f.Name);
        }
      }
      var r = new FormalInfo {
        name = f.Name,
        typ = GetTypeRefInfo(f.Type),
        doc = fdoc,
        token = GetTokenInfo(f.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private TypeParamInfo GetTypeParamInfo(TypeParameter tp, Doc doc) {
      Console.WriteLine("Type parameter");
      var r = new TypeParamInfo {
        name = tp.Name,
        doc = doc == null || doc.tparams == null ? null : doc.tparams[tp.Name],
        token = GetTokenInfo(tp.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }
    
    private CtorInfo GetCtorInfo(DatatypeCtor ctor) {
      Console.WriteLine("Datatype constructor: " + ctor.Name);
      var r = new CtorInfo {
        name = ctor.Name,
        vparams = ctor.Formals.Select(f => GetFormalInfo(f, null, false)).ToList(),
        token = GetTokenInfo(ctor.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private TypeRefInfo GetTypeRefInfo(Microsoft.Dafny.Type t) {
      Console.WriteLine("Type reference");
      Console.WriteLine(t.GetType());
      if (t.IsRevealableType && t.AsRevealableType is TypeSynonymDecl ts && ts.tok != null && ts.tok.filename != null) {
        Console.WriteLine("type synonym");
        Console.WriteLine(ts.Name);
        Console.WriteLine(ts.tok);
        Console.WriteLine(GetTokenInfo(ts.tok));
        Console.WriteLine(t.TypeArgs);
        return new TypeRefInfo {
          name = ts.Name,
          target = GetTokenInfo(ts.tok),
          tparams = t.TypeArgs.Select(GetTypeRefInfo).ToList(),
        };
      }
      if (t.IsArrayType) {
        Console.WriteLine("array type");
        var at = t.AsArrayType;
        foreach (var tp in at.TypeArgs) {
          Console.WriteLine(tp);
        }
        Console.WriteLine(t.ToString());
        return new TypeRefInfo {
          name = "array",
          tparams = { GetTypeRefInfo(t.TypeArgs[0]) },
        };
      }
      if (t.AsCollectionType != null) {
        Console.WriteLine("collection type");
        var ct = t.AsCollectionType;
        return new TypeRefInfo {
          name = ct.CollectionTypeName,
          tparams = t.TypeArgs.Select(GetTypeRefInfo).ToList(),
        };
      }
      if (t.IsArrowType) {
        Console.WriteLine("arrow type");
        var at = t.AsArrowType;
        return new TypeRefInfo {
          name = at.Name,
          tparams = t.TypeArgs.Select(GetTypeRefInfo).ToList(),
          special = "Function",
        };
      }
      if (t.IsTypeParameter) {
        Console.WriteLine("type parameter");
        var tp = t.AsTypeParameter;
        return new TypeRefInfo {
          name = tp.Name,
          target = GetTokenInfo(tp.tok),
          tparams = new TypeRefInfo[0],
        };
      }
      if (t.IsDatatype) {
        Console.WriteLine("datatype");
        var dt = t.AsDatatype;
        if (dt is TupleTypeDecl tt) {
          return new TypeRefInfo {
            name = dt.Name,
            tparams = t.TypeArgs.Select(GetTypeRefInfo).ToList(),
            special = "Tuple",
          };
        }
      }
      if (t.IsNonNullRefType) {
        Console.WriteLine("ref type");
        var udt = t.AsNonNullRefType;
        return new TypeRefInfo {
          name = udt.Name,
          target = GetTokenInfo(udt.tok),
          tparams = t.TypeArgs.Select(GetTypeRefInfo).ToList(),
        };
      }
      Console.WriteLine("other type");
      return new TypeRefInfo {
        name = t.ToString(),
        tparams = new TypeRefInfo[0],
      };
    }

    private TokenInfo GetTokenInfo(IToken tok) {
      return new TokenInfo {
        file = tok.filename.Split('[')[0],
        line = tok.line,
        col = tok.col,
      };
    }

    private ModuleInfo GetModuleInfo(ModuleDefinition mod, string docComment = null) {
      Console.WriteLine("Module: " + mod.Name);
      var r = new ModuleInfo {
        name = mod.Name,
        modifiers = mod.IsAbstract ? new[] { "abstract" } : new string[] { },
        refines = mod.RefinementBaseName?.val,
        decls = this.CollectDecls(mod).Select(GetDeclInfo).Where(d => d != null).ToList(),
        doc = (new Doc(docComment)).body,
        token = GetTokenInfo(mod.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private ImportInfo GetImportInfo(AliasModuleDecl am) {
      Console.WriteLine("Import: " + am.Name);
      var r = new ImportInfo {
        target = GetTokenInfo(am.Module.tok),
        opened = am.Opened,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private ClassInfo GetClassInfo(ClassDecl cl) {
      Console.WriteLine("Class: " + cl.Name);
      var doc = new Doc(cl.DocComment);
      var r = new ClassInfo {
        name = cl.Name,
        isTrait = cl is TraitDecl,
        tparams = cl.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        xtnds = cl.ParentTraits.Select(t => t.ToString()).ToList(),
        members = cl.Members.Select(m => GetDeclInfo(m)).ToList(),
        doc = doc.body,
        token = GetTokenInfo(cl.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private DatatypeInfo GetDatatypeInfo(DatatypeDecl dt) {
      Console.WriteLine("Datatype: " + dt.Name);
      var doc = new Doc(dt.DocComment);
      var r = new DatatypeInfo {
        name = dt.Name,
        isCodata = dt is CoDatatypeDecl,
        tparams = dt.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        ctors = dt.Ctors.Select(GetCtorInfo).ToList(),
        doc = doc.body,
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private TypeSynonymInfo GetTypeSynonymInfo(TypeSynonymDecl ts) {
      Console.WriteLine("Type synonym (TypeSynonymDecl): " + ts.Name);
      var doc = new Doc(ts.DocComment);
      var r = new TypeSynonymInfo {
        name = ts.Name,
        tparams = ts.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        rhs = ts.Rhs.ToString(),
        doc = doc.body,
        token = GetTokenInfo(ts.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    } 

    private TypeSynonymInfo GetTypeSynonymInfo(OpaqueTypeDecl ot) {
      Console.WriteLine("Type synonym (OpaqueTypeDecl): " + ot.Name);
      var doc = new Doc(ot.DocComment);
      var r = new TypeSynonymInfo {
        name = ot.Name,
        tparams = ot.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        rhs = null,
        doc = doc.body,
        token = GetTokenInfo(ot.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private NewtypeInfo GetNewtypeInfo(NewtypeDecl nt) {
      Console.WriteLine("Newtype: " + nt.Name);
      var doc = new Doc(nt.DocComment);
      var r = new NewtypeInfo {
        name = nt.Name,
        btyp = nt.BaseType.ToString(),
        constraint = Printer.ExprToString(nt.Constraint),
        doc = doc.body,
        token = GetTokenInfo(nt.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private FunctionInfo GetFunctionInfo(Function f) {
      Console.WriteLine("Function: " + f.Name);
      var modifiers = new List<string>();
      var doc = new Doc(f.DocComment);
      if (f.IsGhost) modifiers.Add("ghost");
      if (f.IsStatic) modifiers.Add("static");
      if (f.IsProtected) modifiers.Add("protected");
      var spec = new List<SpecInfo>();
      f.Req.ForEach(s => spec.Add(GetSpecInfo(s, "requires")));
      f.Ens.ForEach(s => spec.Add(GetSpecInfo(s, "ensures")));
      f.Reads.ForEach(s => spec.Add(GetSpecInfo(s, "reads")));
      spec.Add(GetSpecInfo(f.Decreases, "decreases"));
      var r = new FunctionInfo {
        name = f.Name,
        kind =
          f is CoPredicate ? "copredicate"
          : f is InductivePredicate ? "inductive predicate"
          : f is Predicate || f is TwoStatePredicate ?
            f.IsGhost ? "predicate"
            : "predicate_method" 
          : f.IsGhost ? "function"
          : "function method",
        modifiers = modifiers,
        tparams = f.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        vparams = f.Formals.Select(vp => GetFormalInfo(vp, doc, false)).ToList(),
        rtyp = f.ResultType.ToString(),
        spec = spec,
        doc = doc.body,
        token = GetTokenInfo(f.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private MethodInfo GetMethodInfo(Method m) {
      Console.WriteLine("Method: " + m.Name);
      var doc = new Doc(m.DocComment);
      var modifiers = new List<string>();
      if (m.IsGhost) modifiers.Add("ghost");
      if (m.IsStatic) modifiers.Add("static");
      var spec = new List<SpecInfo>();
      m.Req.ForEach(s => spec.Add(GetSpecInfo(s, "requires")));
      m.Ens.ForEach(s => spec.Add(GetSpecInfo(s, "ensures")));
      spec.Add(GetSpecInfo(m.Mod, "modifies"));
      spec.Add(GetSpecInfo(m.Decreases, "decreases"));
      var r = new MethodInfo {
        name = m.Name,
        kind =
          m is Constructor ? "constructor"
          : m is InductiveLemma ? "inductive lemma"
          : m is CoLemma ? "colemma"
          : m is Lemma || m is TwoStateLemma ? "lemma"
          : "method",
        modifiers = modifiers,
        tparams = m.TypeArgs.Select(tp => GetTypeParamInfo(tp, doc)).ToList(),
        vparams = m.Ins.Select(vp => GetFormalInfo(vp, doc, false)).ToList(),
        returns = m.Outs.Select(vp => GetFormalInfo(vp, doc, true)).ToList(),
        spec = spec,
        doc = doc.body,
        token = GetTokenInfo(m.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }

    private FieldInfo GetFieldInfo(Field f) {
      Console.WriteLine("Field: " + f.Name);
      var doc = new Doc(f.DocComment);
      var r = new FieldInfo {
        name = f.Name,
        typ = f.Type.ToString(),
        doc = doc.body,
        token = GetTokenInfo(f.tok),
      };
      Console.WriteLine(ConvertToJson(r));
      return r;
    }
  }

  [Serializable]
  [DataContract]
  public class DeclInfo { }

  [Serializable]
  [DataContract]
  public class SpecInfo {
    [DataMember]
    public string kind;
    [DataMember]
    public string clause;
    [DataMember]
    public string doc;
  }

  [Serializable]
  [DataContract]
  public class FormalInfo {
    [DataMember]
    public string name;
    [DataMember]
    public TypeRefInfo typ;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class TypeParamInfo {
    [DataMember]
    public string name;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class CtorInfo {
    [DataMember]
    public string name;
    [DataMember]
    public ICollection<FormalInfo> vparams;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class TypeRefInfo {
    [DataMember]
    public string name;
    [DataMember]
    public TokenInfo target;
    [DataMember]
    public ICollection<TypeRefInfo> tparams;
    [DataMember]
    public string special;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class DocInfo {
    [DataMember]
    public string body;
    [DataMember]
    public ICollection<DocLinkInfo> links;
  }

  [Serializable]
  [DataContract]
  public class DocLinkInfo {
    [DataMember]
    public string identifier;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class TokenInfo {
    [DataMember]
    public string file;
    [DataMember]
    public int line;
    [DataMember]
    public int col;
  }

  /////////////
  // MODULES //
  /////////////

  [Serializable]
  [DataContract]
  [KnownType(typeof(ModuleInfo))]
  [KnownType(typeof(ImportInfo))]
  [KnownType(typeof(ClassInfo))]
  [KnownType(typeof(DatatypeInfo))]
  [KnownType(typeof(TypeSynonymInfo))]
  [KnownType(typeof(NewtypeInfo))]
  [KnownType(typeof(FunctionInfo))]
  [KnownType(typeof(MethodInfo))]
  [KnownType(typeof(FieldInfo))]
  public class FileInfo: DeclInfo {
    [DataMember]
    public string path;
    [DataMember]
    public ICollection<DeclInfo> decls;
    [DataMember]
    public ICollection<string> includes;
    [DataMember]
    public ICollection<ImportInfo> imports;
  }

  [Serializable]
  [DataContract]
  [KnownType(typeof(ModuleInfo))]
  [KnownType(typeof(ImportInfo))]
  [KnownType(typeof(ClassInfo))]
  [KnownType(typeof(DatatypeInfo))]
  [KnownType(typeof(TypeSynonymInfo))]
  [KnownType(typeof(NewtypeInfo))]
  [KnownType(typeof(FunctionInfo))]
  [KnownType(typeof(MethodInfo))]
  [KnownType(typeof(FieldInfo))]
  public class ModuleInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public ICollection<string> modifiers;
    [DataMember]
    public string refines;
    [DataMember]
    public ICollection<DeclInfo> decls;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
    [DataMember]
    public ICollection<ImportInfo> imports;
  }

  [Serializable]
  [DataContract]
  public class ImportInfo: DeclInfo {
    [DataMember]
    public TokenInfo target;
    [DataMember]
    public bool opened;
  }

  [Serializable]
  [DataContract]
  [KnownType(typeof(FunctionInfo))]
  [KnownType(typeof(MethodInfo))]
  [KnownType(typeof(FieldInfo))]
  public class ClassInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public bool isTrait;
    [DataMember]
    public ICollection<TypeParamInfo> tparams;
    [DataMember]
    public ICollection<string> xtnds;
    [DataMember]
    public ICollection<DeclInfo> members;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class DatatypeInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public bool isCodata;
    [DataMember]
    public ICollection<TypeParamInfo> tparams;
    [DataMember]
    public ICollection<CtorInfo> ctors;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class TypeSynonymInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public ICollection<TypeParamInfo> tparams;
    [DataMember]
    public string rhs;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class NewtypeInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public string btyp;
    [DataMember]
    public string constraint;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class FunctionInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public string kind;
    [DataMember]
    public ICollection<string> modifiers;
    [DataMember]
    public ICollection<TypeParamInfo> tparams;
    [DataMember]
    public ICollection<FormalInfo> vparams;
    [DataMember]
    public string rtyp;
    [DataMember]
    public ICollection<SpecInfo> spec;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class MethodInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public string kind;
    [DataMember]
    public ICollection<string> modifiers;
    [DataMember]
    public ICollection<TypeParamInfo> tparams;
    [DataMember]
    public ICollection<FormalInfo> vparams;
    [DataMember]
    public ICollection<FormalInfo> returns;
    [DataMember]
    public ICollection<SpecInfo> spec;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

  [Serializable]
  [DataContract]
  public class FieldInfo: DeclInfo {
    [DataMember]
    public string name;
    [DataMember]
    public string typ;
    [DataMember]
    public string doc;
    [DataMember]
    public TokenInfo token;
  }

}
