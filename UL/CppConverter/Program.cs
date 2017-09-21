﻿using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CppConverter
{

    class Model
    {
        public static Dictionary<string, Metadata.DB_Type> types;

        static Metadata.DB_Type currentType;
        //当前函数的本地变量和参数
        static Stack<Dictionary<string, Metadata.DB_Type>> stackLocalVariables = new Stack<Dictionary<string, Metadata.DB_Type>>();

        public static Metadata.DB_Type GetType(string full_name)
        {
            if (types.ContainsKey(full_name))
            {
                return types[full_name];
            }

            string genegicName = Metadata.DB_Type.GetGenericDefinitionName(full_name);
            if (types.ContainsKey(genegicName))
            {
                return Metadata.DB_Type.MakeGenericType(types[genegicName], Metadata.DB_Type.ParseGenericParameters(full_name));
            }

            string name, declare_type;
            if (Metadata.DB_Type.GetDeclareTypeName(full_name, out declare_type, out name))
            {
                Metadata.DB_Type declareType = types[declare_type];
                if (declareType.is_generic_type_definition)
                {
                    Metadata.DB_Type.GenericParameterDefinition typeDef = declareType.generic_parameter_definitions.Find((a) => { return a.type_name == name; });
                    return Metadata.DB_Type.MakeGenericParameterType(declareType, typeDef);
                }
            }

            return null;
        }

        public static Metadata.DB_Type FindVariable(string name)
        {
            //查找本地变量
            foreach (var v in stackLocalVariables)
            {
                if (v.ContainsKey(name))
                    return v[name];
            }

            //查找成员变量
            if (currentType != null)
            {
                if (currentType.members.ContainsKey(name))
                    return GetType(currentType.members[name].typeName);
                //查找泛型
                if (currentType.is_generic_type_definition)
                {
                    foreach (var gd in currentType.generic_parameter_definitions)
                    {
                        if (gd.type_name == name)
                        {
                            return Metadata.DB_Type.MakeGenericParameterType(currentType, gd);
                        }
                    }
                }
                //当前命名空间查找
                foreach (var nsName in currentType.usingNamespace)
                {
                    string full_name = nsName + "." + name;
                    Metadata.DB_Type type = GetType(full_name);
                    if (type != null)
                        return type;
                }
            }

            return null;
        }

        public static void EnterType(Metadata.DB_Type type)
        {
            currentType = type;
        }
        public static void LeaveType()
        {
            currentType = null;
        }

        public static void EnterBlock()
        {
            stackLocalVariables.Push(new Dictionary<string, Metadata.DB_Type>());
        }

        public static void LeaveBlock()
        {
            stackLocalVariables.Pop();
        }

        public static void AddLocal(string name, Metadata.DB_Type type)
        {
            stackLocalVariables.Peek().Add(name, type);
        }
    }

    class Program
    {
        
        static string GetExpType(Metadata.Expression.Exp exp)
        {
            if(exp is Metadata.Expression.ConstExp)
            {
                Metadata.Expression.ConstExp e = exp as Metadata.Expression.ConstExp;
                long long_v;
                if(long.TryParse(e.value,out long_v))
                {
                    return "System.Int64";
                }

                int int_v;
                if(int.TryParse(e.value,out int_v))
                {
                    return "System.Int32";
                }


                return "System.String";
            }

            if(exp is Metadata.Expression.FieldExp)
            {
                Metadata.Expression.FieldExp e = exp as Metadata.Expression.FieldExp;
                if (e.Caller != null)
                {
                    Metadata.DB_Type caller_type =   Model.GetType(GetExpType(e.Caller));
                    return caller_type.members[e.Name].typeName;
                }
                return Model.FindVariable(e.Name).full_name;
            }

            if(exp is Metadata.Expression.MethodExp)
            {
                return GetExpType(((Metadata.Expression.MethodExp)(exp)).Caller);
            }

            if(exp is Metadata.Expression.ObjectCreateExp)
            {
                Metadata.Expression.ObjectCreateExp e = exp as Metadata.Expression.ObjectCreateExp;
                return e.Type;
            }

            return "";
        }

        static public HashSet<string> GetTypeDependences(Metadata.DB_Type type)
        {
            HashSet<string> result = new HashSet<string>();
            if (!string.IsNullOrEmpty(type.base_type))
                result.Add(type.base_type);
            foreach (var m in type.members.Values)
            {
                if (m.member_type == (int)Metadata.MemberTypes.Field)
                {
                    result.Add(m.field_type_fullname);
                }
                else if (m.member_type == (int)Metadata.MemberTypes.Method)
                {
                    if (!string.IsNullOrEmpty(m.method_ret_type))
                        result.Add(m.method_ret_type);
                    foreach (var a in m.method_args)
                    {
                        result.Add(a.type_fullname);
                    }
                }
            }

            return result;
        }

        static public HashSet<string> GetMethodBodyDependences(Metadata.DB_BlockSyntax methodBody)
        {
            HashSet<string> result = new HashSet<string>();



            return result;
        }

        static void LoadTypeDependences(string full_name, Dictionary<string, Metadata.DB_Type> loaded)
        {
            Metadata.DB_Type type = Metadata.DB.LoadType(full_name, _con);
            if (type == null)
                return;
            loaded.Add(type.full_name, type);
            HashSet<string> dep = GetTypeDependences(type);
            foreach(var t in dep)
            {
                string database_type = Metadata.DB_Type.GetGenericDefinitionName(t);
                if (!loaded.ContainsKey(database_type))
                {
                    LoadTypeDependences(database_type, loaded);
                }
            }
        }

        static OdbcConnection _con;
        static StringBuilder sb = new StringBuilder();
        static int depth;
        static string outputDir;
        



        static string GetCppTypeName(Metadata.DB_Type type)
        {
            if (type.is_generic_paramter)
                return type.name;
            if (type.is_class)
                return type._namespace + "::" + type.name;
            if (type.is_generic_type)
            {
                
                StringBuilder sb = new StringBuilder();
                sb.Append(type._namespace);
                sb.Append("::");
                sb.Append(type.name);
                sb.Append("<");
                for (int i = 0; i < type.generic_parameters.Count; i++)
                {
                    sb.Append(GetCppTypeName(Model.GetType(type.generic_parameters[i])));
                    if (i < type.generic_parameters.Count - 1)
                        sb.Append(",");
                }
                sb.Append(">");
                return sb.ToString();
            }
            return type.full_name;
        }

        static void Main(string[] args)
        {
            string TypeName = args[0];
            outputDir = args[1];
            using (OdbcConnection con = new OdbcConnection("Dsn=MySql;Database=ul"))
            {
                con.Open();
                _con = con;
                //Metadata.DB_Type type = Metadata.DB.LoadType("HelloWorld.Program", _con);
                Model.types = new Dictionary<string, Metadata.DB_Type>();
                LoadTypeDependences(TypeName, Model.types);


                foreach (var t in Model.types.Values)
                {
                    ConvertType(t);
                }

                
            }
        }

        static void AppendDepth()
        {
            for(int i=0;i<depth;i++)
            {
                sb.Append("\t");
            }
        }

        static void AppendLine(string msg)
        {
            AppendDepth();
            sb.AppendLine(msg);
        }

        static void Append(string msg)
        {
            AppendDepth();
            sb.Append(msg);
        }

        static void ConvertType(Metadata.DB_Type type)
        {
            //头文件
            {
                sb.Clear();
                sb.AppendLine("#pragma once");
                //包含头文件
                HashSet<string> depTypes = GetTypeDependences(type);
                foreach(var t in depTypes)
                {
                    Metadata.DB_Type depType = Model.GetType(t);
                    if (!depType.is_generic_paramter && t !=type.full_name)
                        sb.AppendLine("#include \"" + depType.name + ".h\"");
                }
                sb.AppendLine(string.Format("namespace {0}{{", type._namespace));
                {
                    depth++;
                    if (type.is_generic_type_definition)
                    {
                        Append("template<");
                        for (int i = 0; i < type.generic_parameter_definitions.Count; i++)
                        {
                            sb.Append("class "+ type.generic_parameter_definitions[i].type_name);
                            if (i < type.generic_parameter_definitions.Count - 1)
                                sb.Append(",");
                        }
                        sb.AppendLine(">");
                    }
                    AppendLine(string.Format("class {0}{{", type.name));
                    {
                        depth++;

                        foreach (var m in type.members.Values)
                        {
                            ConvertMemberHeader(m);
                        }


                        depth--;
                    }

                    AppendLine("};");
                    depth--;
                }

                sb.AppendLine("}");

                System.IO.File.WriteAllText(System.IO.Path.Combine(outputDir, type.name + ".h"), sb.ToString());
            }

            //cpp文件
            {
                sb.Clear();
                sb.AppendLine("#include \"stdafx.h\"");
                sb.AppendLine("#include \"" + type.name + ".h\"");
                //sb.AppendLine(string.Format("namespace {0}{{", type._namespace));

                foreach (var m in type.members.Values)
                {
                    ConvertMemberCpp(m);
                }

                //sb.AppendLine("}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(outputDir, type.name + ".cpp"), sb.ToString());
            }
        }

        static string GetModifierString(int modifier)
        {
            switch((Metadata.Modifier) modifier)
            {
                case Metadata.Modifier.Private:
                    return "private";
                case Metadata.Modifier.Protected:
                    return "protected";
                case Metadata.Modifier.Public:
                    return "public";
            }

            return "";
        }

        static void ConvertMemberHeader(Metadata.DB_Member member)
        {
            if(member.member_type == (int)Metadata.MemberTypes.Field)
            {
                AppendLine(GetModifierString(member.modifier) + ":");
                if (member.is_static)
                    Append("static ");
                else
                    Append("");
                AppendLine(string.Format("{0} {1};",GetCppTypeName(Model.GetType(member.field_type_fullname)), member.name));
            }
            else if(member.member_type == (int)Metadata.MemberTypes.Method)
            {
                AppendLine(GetModifierString(member.modifier) + ":");
                if (member.is_static)
                    Append("static ");
                else
                    Append("");
                sb.Append(string.Format("{1} {2}","",  string.IsNullOrEmpty(member.method_ret_type)?"void": member.method_ret_type, member.name));
                sb.Append("(");
                if(member.method_args!=null)
                {
                    for (int i = 0; i < member.method_args.Length; i++)
                    {
                        sb.Append(string.Format("{0} {1} {2}",GetCppTypeName(Model.GetType( member.method_args[i].type_fullname)),member.method_args[i].is_ref?"&":"", member.method_args[i].name));
                        if (i < member.method_args.Length-1)
                            sb.Append(",");
                    }
                }
                sb.AppendLine(");");

                //ConvertStatement(member.method_body);
            }
        }

        static void ConvertMemberCpp(Metadata.DB_Member member)
        {
            if (member.member_type == (int)Metadata.MemberTypes.Field)
            {
                if(member.is_static)
                {
                    AppendLine(GetCppTypeName(Model.GetType(member.field_type_fullname))+ " "+ GetCppTypeName(Model.GetType(member.declaring_type)) + "::" + member.name+";");
                }
            }
            else if (member.member_type == (int)Metadata.MemberTypes.Method)
            {
                sb.Append(string.Format("{0} {1}::{2}", string.IsNullOrEmpty(member.method_ret_type) ? "void" : member.method_ret_type, GetCppTypeName(Model.GetType(member.declaring_type)), member.name));
                sb.Append("(");
                if (member.method_args != null)
                {
                    for (int i = 0; i < member.method_args.Length; i++)
                    {
                        sb.Append(string.Format("{0} {1} {2}", GetCppTypeName(Model.GetType(member.method_args[i].type_fullname)), member.method_args[i].is_ref ? "&" : "", member.method_args[i].name));
                        if (i < member.method_args.Length - 1)
                            sb.Append(",");
                    }
                }
                sb.AppendLine(")");

                ConvertStatement(member.method_body);
            }
        }

        static void ConvertStatement(Metadata.DB_StatementSyntax ss)
        {
            if(ss is Metadata.DB_BlockSyntax)
            {
                ConvertStatement((Metadata.DB_BlockSyntax)ss);
            }
            else if(ss is Metadata.DB_IfStatementSyntax)
            {
                ConvertStatement((Metadata.DB_IfStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_ExpressionStatementSyntax)
            {
                ConvertStatement((Metadata.DB_ExpressionStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_LocalDeclarationStatementSyntax)
            {
                ConvertStatement((Metadata.DB_LocalDeclarationStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_ForStatementSyntax)
            {
                ConvertStatement((Metadata.DB_ForStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_DoStatementSyntax)
            {
                ConvertStatement((Metadata.DB_DoStatementSyntax)ss);
            }
            else if (ss is Metadata.DB_WhileStatementSyntax)
            {
                ConvertStatement((Metadata.DB_WhileStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_SwitchStatementSyntax)
            {
                ConvertStatement((Metadata.DB_SwitchStatementSyntax)ss);
            }
            else if(ss is Metadata.DB_BreakStatementSyntax)
            {
                AppendLine("break;");
            }
            else if(ss is Metadata.DB_ReturnStatementSyntax)
            {
                AppendLine("return "+ExpressionToString(((Metadata.DB_ReturnStatementSyntax)ss).Expression) +";");
            }
            else
            {
                Console.Error.WriteLine("不支持的语句 " + ss.GetType().ToString());
            }
        }

        static void ConvertStatement(Metadata.DB_BlockSyntax bs)
        {
            AppendLine("{");
            depth++;
            foreach(var s in bs.List)
            {
                ConvertStatement(s);
            }
            depth--;
            AppendLine("}");
        }

        static void ConvertStatement(Metadata.DB_IfStatementSyntax bs)
        {
            AppendLine("if("+ ExpressionToString(bs.Condition)+")");
            ConvertStatement(bs.Statement);
            if(bs.Else!=null)
            {
                AppendLine("else");
                ConvertStatement(bs.Else);
            }
        }

        static void ConvertStatement(Metadata.DB_ExpressionStatementSyntax bs)
        {
            AppendLine(ExpressionToString(bs.Exp)+";");
        }

        static void ConvertStatement(Metadata.DB_LocalDeclarationStatementSyntax bs)
        {
            Append(bs.Type + " ");
            for(int i=0;i<bs.Variables.Count;i++)
            {
                sb.Append(ExpressionToString(bs.Variables[i]));
                if(i<bs.Variables.Count-2)
                {
                    sb.Append(",");
                }
            }
            sb.AppendLine(";");
        }
        static void ConvertStatement(Metadata.DB_ForStatementSyntax bs)
        {
            Append("for(");
            sb.Append(ExpressionToString(bs.Declaration));
            sb.Append(";");
            sb.Append(ExpressionToString(bs.Condition));
            sb.Append(";");
            
            for (int i = 0; i < bs.Incrementors.Count; i++)
            {
                sb.Append(ExpressionToString(bs.Incrementors[i]));
                if (i < bs.Incrementors.Count - 2)
                {
                    sb.Append(",");
                }
            }
            sb.AppendLine(")");
            ConvertStatement(bs.Statement);
        }

        static void ConvertStatement(Metadata.DB_DoStatementSyntax bs)
        {
            AppendLine("do");
            ConvertStatement(bs.Statement);
            Append("while");
            sb.Append("(");
            sb.Append(ExpressionToString(bs.Condition));
            sb.AppendLine(");");
        }
        static void ConvertStatement(Metadata.DB_WhileStatementSyntax bs)
        {
            Append("while");
            sb.Append("(");
            sb.Append(ExpressionToString(bs.Condition));
            sb.AppendLine(")");
            ConvertStatement(bs.Statement);
        }

        static void ConvertStatement(Metadata.DB_SwitchStatementSyntax bs)
        {
            Append("switch");
            sb.Append("(");
            sb.Append(ExpressionToString(bs.Expression));
            sb.AppendLine(")");
            AppendLine("{");
            depth++;
            for (int i=0;i<bs.Sections.Count;i++)
            {
                ConvertSwitchSection(bs.Sections[i]);
            }
            depth--;
            AppendLine("}");
        }
        static void ConvertSwitchSection(Metadata.DB_SwitchStatementSyntax.SwitchSectionSyntax bs)
        {
            for(int i=0;i<bs.Labels.Count;i++)
            {
                AppendLine("case " + ExpressionToString(bs.Labels[i]) + ":");
            }

            for(int i=0;i<bs.Statements.Count;i++)
            {
                ConvertStatement(bs.Statements[i]);
            }
        }


        static string ExpressionToString(Metadata.Expression.Exp es)
        {
            if(es is Metadata.Expression.ConstExp)
            {
                return ExpressionToString((Metadata.Expression.ConstExp)es);
            }
            else if(es is Metadata.Expression.FieldExp)
            {
                return ExpressionToString((Metadata.Expression.FieldExp)es);
            }
            else if(es is Metadata.Expression.MethodExp)
            {
                return ExpressionToString((Metadata.Expression.MethodExp)es);
            }
            //else if(es is Metadata.DB_MemberAccessExpressionSyntax)
            //{
            //    return ExpressionToString((Metadata.DB_MemberAccessExpressionSyntax)es);
            //}
            else if (es is Metadata.Expression.ObjectCreateExp)
            {
                return ExpressionToString((Metadata.Expression.ObjectCreateExp)es);
            }
            //else if(es is Metadata.DB_ArgumentSyntax)
            //{
            //    return ExpressionToString((Metadata.DB_ArgumentSyntax)es);
            //}
            //else if(es is Metadata.DB_IdentifierNameSyntax)
            //{
            //    return ExpressionToString((Metadata.DB_IdentifierNameSyntax)es);
            //}
            else
            {
                Console.Error.WriteLine("不支持的表达式 " + es.GetType().Name);
            }
            return "";
        }

        //static string ExpressionToString(Metadata.DB_InitializerExpressionSyntax es)
        //{
        //    StringBuilder ExpSB = new StringBuilder();
        //    if(es.Expressions.Count>0)
        //    {
        //        ExpSB.Append("(");
        //    }

        //    for(int i=0;i<es.Expressions.Count;i++)
        //    {
        //        ExpSB.Append(ExpressionToString(es.Expressions[i]));
        //        if (i < es.Expressions.Count - 2)
        //            ExpSB.Append(",");
        //    }

        //    if (es.Expressions.Count > 0)
        //    {
        //        ExpSB.Append(")");
        //    }

        //    return ExpSB.ToString();
        //}
        static string ExpressionToString(Metadata.Expression.MethodExp es)
        {
            StringBuilder ExpSB = new StringBuilder();
            ExpSB.Append(ExpressionToString(es.Caller));
            ExpSB.Append("(");
            if (es.Args != null)
            {
                for (int i = 0; i < es.Args.Count; i++)
                {
                    ExpSB.Append(ExpressionToString(es.Args[i]));
                    if (i < es.Args.Count - 2)
                        ExpSB.Append(",");
                }
            }
            ExpSB.Append(")");

            return ExpSB.ToString();
        }
        static string ExpressionToString(Metadata.Expression.ConstExp es)
        {
            return es.value;
        }
        static string ExpressionToString(Metadata.Expression.FieldExp es)
        {
            if(es.Caller == null)   //本地变量或者类变量，或者全局类
            {
                return es.Name;
            }
            else
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append(ExpressionToString(es.Caller));

                Metadata.DB_Type caller_type = Model.GetType(GetExpType(es.Caller));
                if(caller_type!=null)
                {
                    if(caller_type.is_class)
                    {
                        stringBuilder.Append("->");
                    }
                    else
                    {
                        stringBuilder.Append(".");
                    }
                }
                else
                {
                    stringBuilder.Append("::");
                }
                stringBuilder.Append(es.Name);
                return stringBuilder.ToString();
            }
        }
        static string ExpressionToString(Metadata.Expression.ObjectCreateExp es)
        {
            StringBuilder ExpSB = new StringBuilder();
            ExpSB.Append("new ");
            ExpSB.Append(es.Type);
            ExpSB.Append("(");
            if (es.Args != null)
            {
                for (int i = 0; i < es.Args.Count; i++)
                {
                    ExpSB.Append(ExpressionToString(es.Args[i]));
                    if (i < es.Args.Count - 2)
                        ExpSB.Append(",");
                }
            }
            ExpSB.Append(")");
            return ExpSB.ToString();
        }
        //static string ExpressionToString(Metadata.DB_ArgumentSyntax es)
        //{
        //    return ExpressionToString(es.Expression);
        //}
        //static string ExpressionToString(Metadata.DB_IdentifierNameSyntax es)
        //{
        //    return es.Name;
        //}

        static string ExpressionToString(Metadata.VariableDeclaratorSyntax es)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(es.Identifier);
            if(es.Initializer!=null)
            {
                stringBuilder.Append("=");
                stringBuilder.Append(ExpressionToString(es.Initializer));
            }

            return stringBuilder.ToString();
        }

        static string ExpressionToString(Metadata.VariableDeclarationSyntax es)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(es.Type);
            stringBuilder.Append(" ");
            for (int i=0;i<es.Variables.Count;i++)
            {
                stringBuilder.Append(ExpressionToString(es.Variables[i]));
                if (i < es.Variables.Count - 1)
                    stringBuilder.Append(",");
            }
            return stringBuilder.ToString();
        }
    }

}
