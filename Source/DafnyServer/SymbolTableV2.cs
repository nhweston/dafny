using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Dafny;
using Type = Microsoft.Dafny.Type;
using Function = Microsoft.Dafny.Function;
using Program = Microsoft.Dafny.Program;

namespace DafnyServer {
  
  public class SymbolTableV2 {
    private readonly Program _program;

    public SymbolTableV2(Program program) {
      _program = program;
    }

    public ModuleInfo GetSymbolTree() {
      return GetModuleInfo(_program.DefaultModuleDef);
    }

    private DeclInfo GetDeclInfo(Declaration decl) {
      if (decl is LiteralModuleDecl mod) return GetModuleInfo(mod.ModuleDef);
      if (decl is ClassDecl cl) return GetClassInfo(cl);
      if (decl is DatatypeDecl dt) return GetDatatypeInfo(dt);
      if (decl is TypeSynonymDecl ts) return GetTypeSynonymInfo(ts);
      if (decl is OpaqueTypeDecl ot) return GetTypeSynonymInfo(ot);
      if (decl is NewtypeDecl nt) return GetNewtypeInfo(nt);
      if (decl is Function f) return GetFunctionInfo(f);
      if (decl is Method m) return GetMethodInfo(m);
      if (decl is Field fld) return GetFieldInfo(fld);
      Console.WriteLine("Unknown declaration type");
      return null;
    }

    private SpecInfo GetSpecInfo(MaybeFreeExpression exp, string kind) {
      return new SpecInfo {
        kind = kind,
        clause = Printer.ExprToString(exp.E),
        doc = Doc.Parse(exp.DocComment).Body,
      };
    }

    private SpecInfo GetSpecInfo(FrameExpression exp, string kind) {
      return new SpecInfo {
        kind = kind,
        clause = Printer.ExprToString(exp.E),
        doc = null,
      };
    }

    private SpecInfo GetSpecInfo(Specification<Expression> se, string kind) {
      return new SpecInfo {
        kind = kind,
        clause = string.Join(", ", se.Expressions.Select(e => Printer.ExprToString(e))),
        doc = null,
      };
    }

    private SpecInfo GetSpecInfo(Specification<FrameExpression> se, string kind) {
      return new SpecInfo {
        kind = kind,
        clause = string.Join(", ", se.Expressions.Select(e => Printer.ExprToString(e.E))),
        doc = null,
      };
    }

    private FormalInfo GetFormalInfo(Formal f) {
      return new FormalInfo {
        name = f.Name,
        typ = f.Type.ToString(),
      };
    }
    
    private CtorInfo GetCtorInfo(DatatypeCtor ctor) {
      return new CtorInfo {
        name = ctor.Name,
        vparams = ctor.Formals.Select(GetFormalInfo).ToList(),
      };
    }

    private ModuleInfo GetModuleInfo(ModuleDefinition mod) {
      return new ModuleInfo {
        name = mod.Name,
        modifiers = mod.IsAbstract ? new[] { "abstract" } : new string[] { },
        refines = mod.RefinementBaseName.val,
        decls = mod.TopLevelDecls.Select(GetDeclInfo).ToList(),
      };
    }

    private ClassInfo GetClassInfo(ClassDecl cl) {
      return new ClassInfo {
        name = cl.Name,
        isTrait = cl is TraitDecl,
        tparams = cl.TypeArgs.Select(ta => ta.Name).ToList(),
        xtnds = cl.ParentTraits.Select(t => t.ToString()).ToList(),
        members = cl.Members.Select(m => GetDeclInfo(m)).ToList(),
      };
    }

    private DatatypeInfo GetDatatypeInfo(DatatypeDecl dt) {
      return new DatatypeInfo {
        name = dt.Name,
        isCodata = dt is CoDatatypeDecl,
        tparams = dt.TypeArgs.Select(ta => ta.Name).ToList(),
        ctors = dt.Ctors.Select(GetCtorInfo).ToList(),
      };
    }

    private TypeSynonymInfo GetTypeSynonymInfo(TypeSynonymDecl ts) {
      return new TypeSynonymInfo {
        name = ts.Name,
        tparams = ts.TypeArgs.Select(ta => ta.Name).ToList(),
        rhs = ts.Rhs.ToString(),
      };
    } 

    private TypeSynonymInfo GetTypeSynonymInfo(OpaqueTypeDecl ot) {
      return new TypeSynonymInfo {
        name = ot.Name,
        tparams = ot.TypeArgs.Select(ta => ta.Name).ToList(),
        rhs = null,
      };
    }

    private NewtypeInfo GetNewtypeInfo(NewtypeDecl nt) {
      return new NewtypeInfo {
        name = nt.Name,
        btyp = nt.BaseType.ToString(),
        constraint = Printer.ExprToString(nt.Constraint),
      };
    }

    private FunctionInfo GetFunctionInfo(Function f) {
      var modifiers = new List<string>();
      if (f.IsGhost) modifiers.Add("ghost");
      if (f.IsStatic) modifiers.Add("static");
      if (f.IsProtected) modifiers.Add("protected");
      var spec = new List<SpecInfo>();
      f.Req.ForEach(s => spec.Add(GetSpecInfo(s, "requires")));
      f.Ens.ForEach(s => spec.Add(GetSpecInfo(s, "ensures")));
      f.Reads.ForEach(s => spec.Add(GetSpecInfo(s, "reads")));
      spec.Add(GetSpecInfo(f.Decreases, "requires"));
      return new FunctionInfo {
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
        tparams = f.TypeArgs.Select(ta => ta.Name).ToList(),
        vparams = f.Formals.Select(GetFormalInfo).ToList(),
        rtyp = f.ResultType.ToString(),
        spec = spec,
      };
    }

    private MethodInfo GetMethodInfo(Method m) {
      var modifiers = new List<string>();
      if (m.IsGhost) modifiers.Add("ghost");
      if (m.IsStatic) modifiers.Add("static");
      var spec = new List<SpecInfo>();
      m.Req.ForEach(s => spec.Add(GetSpecInfo(s, "requires")));
      m.Ens.ForEach(s => spec.Add(GetSpecInfo(s, "ensures")));
      spec.Add(GetSpecInfo(m.Mod, "modifies"));
      spec.Add(GetSpecInfo(m.Decreases, "decreases"));
      return new MethodInfo {
        name = m.Name,
        kind =
          m is Constructor ? "constructor"
          : m is InductiveLemma ? "inductive lemma"
          : m is CoLemma ? "colemma"
          : m is Lemma || m is TwoStateLemma ? "lemma"
          : "method",
        modifiers = modifiers,
        tparams = m.TypeArgs.Select(ta => ta.Name).ToList(),
        vparams = m.Ins.Select(GetFormalInfo).ToList(),
        returns = m.Outs.Select(GetFormalInfo).ToList(),
        spec = spec,
      };
    }

    private FieldInfo GetFieldInfo(Field f) {
      return new FieldInfo {
        name = f.Name,
        typ = f.Type.ToString(),
      };
    }

    // private void AddMethods(ModuleDefinition module, List<SymbolInformation> information) {
    //   foreach (
    //       var clbl in
    //       ModuleDefinition.AllCallables(module.TopLevelDecls).Where(e => e != null && !(e.Tok is IncludeToken))) {

    //     if (clbl is Predicate) {
    //       var predicate = clbl as Predicate;
    //       var predicateSymbol = new SymbolInformation {
    //         Module = predicate.EnclosingClass.Module.Name,
    //         Name = predicate.Name,
    //         ParentClass = predicate.EnclosingClass.Name,
    //         SymbolType = SymbolInformation.Type.Predicate,
    //         StartToken = predicate.tok,
    //         EndToken = predicate.BodyEndTok,
    //         DocComment = predicate.DocComment,
    //         Doc = Doc.Parse(predicate.DocComment).Body,
    //       };
    //       information.Add(predicateSymbol);

    //     } else if (clbl is Function) {
    //       var fn = (Function)clbl;
    //       var functionSymbol = new SymbolInformation {
    //         Module = fn.EnclosingClass.Module.Name,
    //         Name = fn.Name,
    //         ParentClass = fn.EnclosingClass.Name,
    //         SymbolType = SymbolInformation.Type.Function,
    //         StartToken = fn.tok,
    //         EndColumn = fn.BodyEndTok.col,
    //         EndLine = fn.BodyEndTok.line,
    //         EndPosition = fn.BodyEndTok.pos,
    //         EndToken = fn.BodyEndTok,
    //         DocComment = fn.DocComment,
    //         Doc = Doc.Parse(fn.DocComment).Body,
    //       };
    //       information.Add(functionSymbol);
    //     } else {
    //       var m = (Method)clbl;
    //       if (m.Body != null && m.Body.Body != null) {
    //         information.AddRange(ResolveCallStatements(m.Body.Body));
    //         information.AddRange(ResolveLocalDefinitions(m.Body.Body, m));
    //       }
    //       var methodSymbol = new SymbolInformation {
    //         Module = m.EnclosingClass.Module.Name,
    //         Name = m.Name,
    //         ParentClass = m.EnclosingClass.Name,
    //         SymbolType = SymbolInformation.Type.Method,
    //         StartToken = m.tok,
    //         Ensures = ParseContracts(m.Ens),
    //         Requires = ParseContracts(m.Req),
    //         References =
    //               FindMethodReferencesInternal(m.EnclosingClass.Module.Name + "." + m.EnclosingClass.Name + "." +
    //                                m.Name),
    //         EndColumn = m.BodyEndTok.col,
    //         EndLine = m.BodyEndTok.line,
    //         EndPosition = m.BodyEndTok.pos,
    //         EndToken = m.BodyEndTok,
    //         DocComment = m.DocComment,
    //         Doc = Doc.Parse(m.DocComment).Body,
    //       };
    //       information.Add(methodSymbol);
    //     }
    //   }
    // }

    // private void AddFields(ModuleDefinition module, List<SymbolInformation> information) {
    //   foreach (
    //       var fs in ModuleDefinition.AllFields(module.TopLevelDecls).Where(e => e != null && !(e.tok is IncludeToken))) {

    //     var fieldSymbol = new SymbolInformation {
    //       Module = fs.EnclosingClass.Module.Name,
    //       Name = fs.Name,
    //       ParentClass = fs.EnclosingClass.Name,
    //       SymbolType = SymbolInformation.Type.Field,
    //       StartToken = fs.tok,
    //       References = FindFieldReferencesInternal(fs.Name, fs.EnclosingClass.Name, fs.EnclosingClass.Module.Name),
    //       DocComment = fs.DocComment,
    //       Doc = Doc.Parse(fs.DocComment).Body,
    //     };
    //     if (fs.Type is UserDefinedType) {
    //       var userType = fs.Type as UserDefinedType;
    //       fieldSymbol.ReferencedClass = userType.ResolvedClass.CompileName;
    //       fieldSymbol.ReferencedModule = userType.ResolvedClass.Module.CompileName;
    //     }
    //     information.Add(fieldSymbol);
    //   }
    // }

    // private static IEnumerable<SymbolInformation> ResolveLocalDefinitions(IEnumerable<Statement> statements, Method method) {
    //   var information = new List<SymbolInformation>();

    //   foreach (var statement in statements) {
    //     if (statement is VarDeclStmt) {
    //       var declarations = (VarDeclStmt)statement;
    //       {
    //         Type type = null;
    //         var rightSide = declarations.Update as UpdateStmt;
    //         if (rightSide != null) {
    //           var definition = rightSide.Rhss.First();
    //           var typeDef = definition as TypeRhs;
    //           if (typeDef != null) {
    //             type = typeDef.Type;
    //           }
    //         }
    //         if (type != null && type is UserDefinedType) {
    //           var userType = type as UserDefinedType;
    //           foreach (var declarationLocal in declarations.Locals) {
    //             var name = declarationLocal.Name;
    //             information.Add(new SymbolInformation {
    //               Name = name,
    //               ParentClass = userType.ResolvedClass.CompileName,
    //               Module = userType.ResolvedClass.Module.CompileName,
    //               SymbolType = SymbolInformation.Type.Definition,
    //               StartToken = method.BodyStartTok,
    //               EndToken = method.BodyEndTok,
    //               DocComment = method.DocComment,
    //             });
    //           }
    //         }
    //       }
    //     }
    //     if (statement is UpdateStmt) {
    //       var updateStatement = statement as UpdateStmt;
    //       var lefts = updateStatement.Lhss;
    //       foreach (var expression in lefts) {
    //         if (expression is AutoGhostIdentifierExpr) {
    //           var autoGhost = expression as AutoGhostIdentifierExpr;
    //           information.Add(new SymbolInformation {
    //             Name = autoGhost.Name,
    //             ParentClass = autoGhost.Resolved.Type.ToString(),
    //             SymbolType = SymbolInformation.Type.Definition,
    //             StartToken = updateStatement.Tok,
    //             EndToken = updateStatement.EndTok,
    //           });
    //         }
    //       }
    //     }
    //     if (statement.SubStatements.Any()) {
    //       information.AddRange(ResolveLocalDefinitions(statement.SubStatements.ToList(), method));
    //     }
    //   }
    //   return information;
    // }

    // private static IEnumerable<SymbolInformation> ResolveCallStatements(IEnumerable<Statement> statements) {
    //   var information = new List<SymbolInformation>();

    //   foreach (var statement in statements) {
    //     if (statement is CallStmt) {
    //       ParseCallStatement(statement, information);
    //     } else if (statement is UpdateStmt) {
    //       ParseUpdateStatement(statement, information);
    //     }

    //     if (statement.SubStatements.Any()) {
    //       information.AddRange(ResolveCallStatements(statement.SubStatements.ToList()));
    //     }
    //   }
    //   return information;
    // }

    // private static void ParseCallStatement(Statement statement, List<SymbolInformation> information) {
    //   var callStmt = (CallStmt)statement;
    //   {
    //     if (!(callStmt.Receiver.Type is UserDefinedType)) return;

    //     var receiver = callStmt.Receiver as NameSegment;
    //     var userType = (UserDefinedType)callStmt.Receiver.Type;
    //     var reveiverName = receiver == null ? "" : receiver.Name;
    //     information.Add(new SymbolInformation {
    //       Name = callStmt.Method.CompileName,
    //       ParentClass = userType.ResolvedClass.CompileName,
    //       Module = userType.ResolvedClass.Module.CompileName,
    //       Call = reveiverName + "." + callStmt.MethodSelect.Member,
    //       SymbolType = SymbolInformation.Type.Call,
    //       StartToken = callStmt.MethodSelect.tok,
    //     });
    //   }
    // }

    // private static void ParseUpdateStatement(Statement statement, List<SymbolInformation> information) {
    //   var updateStmt = (UpdateStmt)statement;
    //   var leftSide = updateStmt.Lhss;
    //   var rightSide = updateStmt.Rhss;
    //   var leftSideDots = leftSide.OfType<ExprDotName>();
    //   var rightSideDots = rightSide.OfType<ExprDotName>();
    //   var allExprDotNames = leftSideDots.Concat(rightSideDots);
    //   foreach (var exprDotName in allExprDotNames) {
    //     if (!(exprDotName.Lhs.Type is UserDefinedType)) continue;

    //     var segment = exprDotName.Lhs as NameSegment;
    //     var type = (UserDefinedType)exprDotName.Lhs.Type;
    //     var designator = segment == null ? "" : segment.Name;
    //     information.Add(new SymbolInformation {
    //       Name = exprDotName.SuffixName,
    //       ParentClass = type.ResolvedClass.CompileName,
    //       Module = type.ResolvedClass.Module.CompileName,
    //       Call = designator + "." + exprDotName.SuffixName,
    //       SymbolType = SymbolInformation.Type.Call,
    //       StartToken = exprDotName.tok,
    //     });
    //   }
    // }

    // private List<ReferenceInformation> FindFieldReferencesInternal(string fieldName, string className,
    //     string moduleName) {
    //   var information = new List<ReferenceInformation>();

    //   foreach (var module in _dafnyProgram.Modules()) {
    //     foreach (var clbl in ModuleDefinition.AllCallables(module.TopLevelDecls).Where(e => !(e.Tok is IncludeToken))) {
    //       if (!(clbl is Method)) continue;

    //       var m = (Method)clbl;
    //       if (m.Body != null) {
    //         information.AddRange(ParseBodyForFieldReferences(m.Body.SubStatements, fieldName, className, moduleName));
    //       }
    //     }
    //   }

    //   return information;
    // }
    // private List<ReferenceInformation> FindMethodReferencesInternal(string methodToFind) {
    //   var information = new List<ReferenceInformation>();

    //   foreach (var module in _dafnyProgram.Modules()) {
    //     foreach (var clbl in ModuleDefinition.AllCallables(module.TopLevelDecls).Where(e => !(e.Tok is IncludeToken))) {
    //       if (!(clbl is Method)) continue;

    //       var m = (Method)clbl;
    //       if (m.Body != null) {
    //         information.AddRange(ParseBodyForMethodReferences(m.Body.SubStatements, methodToFind, m.Name));
    //       }
    //     }
    //   }
    //   return information;
    // }

    // private static ICollection<Expression> GetAllSubExpressions(Expression expression) {
    //   var expressions = new List<Expression>();
    //   foreach (var subExpression in expression.SubExpressions) {
    //     expressions.AddRange(GetAllSubExpressions(subExpression));
    //   }
    //   expressions.Add(expression);
    //   return expressions;
    // }

    // private static IEnumerable<ReferenceInformation> ParseBodyForFieldReferences(IEnumerable<Statement> block, string fieldName, string className, string moduleName) {
    //   var information = new List<ReferenceInformation>();
    //   foreach (var statement in block) {
    //     if (statement is UpdateStmt) {
    //       var updateStmt = (UpdateStmt)statement;
    //       var leftSide = updateStmt.Lhss;
    //       var rightSide = updateStmt.Rhss;
    //       var leftSideDots = leftSide.OfType<ExprDotName>();
    //       var rightSideDots = rightSide.OfType<ExprDotName>();
    //       var exprDotNames = leftSideDots.Concat(rightSideDots);
    //       var leftSideNameSegments = leftSide.OfType<NameSegment>();
    //       var rightSideNameSegments = rightSide.OfType<NameSegment>();
    //       var nameSegments = leftSideNameSegments.Concat(rightSideNameSegments);
    //       var allRightSideExpressions = rightSide.SelectMany(e => e.SubExpressions.SelectMany(GetAllSubExpressions));
    //       var allLeftSideExpressions =
    //           leftSide.SelectMany(e => e.SubExpressions.SelectMany(GetAllSubExpressions));
    //       var allExpressions = allRightSideExpressions.Concat(allLeftSideExpressions).ToList();
    //       var allExprDotNames = exprDotNames.Concat(allExpressions.OfType<ExprDotName>()).Distinct();
    //       var allNameSegments = nameSegments.Concat(allExpressions.OfType<NameSegment>()).Distinct();

    //       foreach (var exprDotName in allExprDotNames) {
    //         if (exprDotName.Lhs.Type is UserDefinedType) {
    //           var type = (UserDefinedType)exprDotName.Lhs.Type;
    //           if (fieldName == exprDotName.SuffixName && className == type.ResolvedClass.CompileName &&
    //               moduleName == type.ResolvedClass.Module.CompileName) {
    //             information.Add(new ReferenceInformation {
    //               MethodName = exprDotName.SuffixName,
    //               StartToken = exprDotName.tok,
    //               ReferencedName = exprDotName.SuffixName

    //             });
    //           }

    //         }
    //       }
    //       foreach (var nameSegment in allNameSegments) {
    //         if (nameSegment.ResolvedExpression is MemberSelectExpr) {
    //           var memberAcc = (MemberSelectExpr)nameSegment.ResolvedExpression;
    //           if (fieldName == memberAcc.MemberName &&
    //               className == memberAcc.Member.EnclosingClass.CompileName &&
    //               moduleName == memberAcc.Member.EnclosingClass.Module.CompileName) {
    //             information.Add(new ReferenceInformation {
    //               MethodName = memberAcc.MemberName,
    //               StartToken = memberAcc.tok,
    //               ReferencedName = memberAcc.MemberName
    //             });
    //           }
    //         }
    //       }
    //     }

    //     if (statement.SubStatements.Any()) {
    //       information.AddRange(ParseBodyForFieldReferences(statement.SubStatements, fieldName, className, moduleName));
    //     }
    //   }
    //   return information;
    // }

    // private List<ReferenceInformation> ParseBodyForMethodReferences(IEnumerable<Statement> block, string methodToFind, string currentMethodName) {
    //   var information = new List<ReferenceInformation>();
    //   foreach (var statement in block) {
    //     if (statement is CallStmt) {
    //       var callStmt = (CallStmt)statement;
    //       if (callStmt.Method.FullName == methodToFind) {
    //         information.Add(new ReferenceInformation {
    //           StartToken = callStmt.MethodSelect.tok,
    //           MethodName = currentMethodName,
    //           ReferencedName = methodToFind.Split('.')[2]
    //         });
    //       }
    //     }
    //     if (statement.SubStatements.Any()) {
    //       information.AddRange(ParseBodyForMethodReferences(statement.SubStatements, methodToFind, currentMethodName));
    //     }
    //   }
    //   return information;
    // }

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
      public string typ;
    }

    [Serializable]
    [DataContract]
    public class CtorInfo {
      [DataMember]
      public string name;
      [DataMember]
      public ICollection<FormalInfo> vparams;
    }

    /////////////
    // MODULES //
    /////////////

    [Serializable]
    [DataContract]
    [KnownType(typeof(ModuleInfo))]
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
      public ICollection<string> tparams;
      [DataMember]
      public ICollection<string> xtnds;
      [DataMember]
      public ICollection<DeclInfo> members;
    }

    [Serializable]
    [DataContract]
    public class DatatypeInfo: DeclInfo {
      [DataMember]
      public string name;
      [DataMember]
      public bool isCodata;
      [DataMember]
      public ICollection<string> tparams;
      [DataMember]
      public ICollection<CtorInfo> ctors;
    }

    [Serializable]
    [DataContract]
    public class TypeSynonymInfo: DeclInfo {
      [DataMember]
      public string name;
      [DataMember]
      public ICollection<string> tparams;
      [DataMember]
      public string rhs;
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
      public ICollection<string> tparams;
      [DataMember]
      public ICollection<FormalInfo> vparams;
      [DataMember]
      public string rtyp;
      [DataMember]
      public ICollection<SpecInfo> spec;
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
      public ICollection<string> tparams;
      [DataMember]
      public ICollection<FormalInfo> vparams;
      [DataMember]
      public ICollection<FormalInfo> returns;
      [DataMember]
      public ICollection<SpecInfo> spec;
    }

    [Serializable]
    [DataContract]
    public class FieldInfo: DeclInfo {
      [DataMember]
      public string name;
      [DataMember]
      public string typ;
    }

  }
}

