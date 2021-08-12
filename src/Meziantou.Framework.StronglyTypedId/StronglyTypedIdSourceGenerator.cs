﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Meziantou.Framework.CodeDom;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.StronglyTypedId;

[Generator]
public sealed partial class StronglyTypedIdSourceGenerator : ISourceGenerator
{
    // Possible improvements
    //
    // - XmlSerializer / NewtonsoftJsonConvert / MongoDbConverter / Elaticsearch / YamlConverter
    // - TypeConverter / IConvertible

    private const string FieldName = "_value";
    private const string PropertyName = "Value";
    private const string PropertyAsStringName = "ValueAsString";

    [SuppressMessage("Usage", "MA0101:String contains an implicit end of line character", Justification = "Not important")]
    private const string AttributeText = @"
// ------------------------------------------------------------------------------
//  <autogenerated>
//      This code was generated by a tool.
//
//      Changes to this file may cause incorrect behavior and will be lost if
//      the code is regenerated.
//  </autogenerated>
// ------------------------------------------------------------------------------

[System.Diagnostics.Conditional(""StronglyTypedId_Attributes"")]
[System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.Class)]
internal sealed class StronglyTypedIdAttribute : System.Attribute
{
    /// <summary>
    /// Indicate the type is a strongly-typed id
    /// </summary>
    /// <param name=""idType"">Type of the generated Value</param>
    /// <param name=""generateSystemTextJsonConverter"">Specify if the <see cref=""System.Text.Json.Serialization.JsonConverter""/> should be generated</param>
    /// <param name=""generateNewtonsoftJsonConverter"">Specify if the <see cref=""Newtonsoft.Json.JsonConverter""/> should be generated</param>
    /// <param name=""generateSystemComponentModelTypeConverter"">Specify if the <see cref=""System.ComponentModel.TypeConverter""/> should be generated</param>
    /// <param name=""generateMongoDBBsonSerialization"">Specify if the <see cref=""MongoDB.Bson.Serialization.Serializers.SerializerBase{T}""/> should be generated</param>
    /// <param name=""addCodeGeneratedAttribute"">Add <see cref=""System.CodeDom.Compiler.GeneratedCodeAttribute""/> to the generated members</param>
    public StronglyTypedIdAttribute(System.Type idType,
                                    bool generateSystemTextJsonConverter = true,
                                    bool generateNewtonsoftJsonConverter = true,
                                    bool generateSystemComponentModelTypeConverter = true,
                                    bool generateMongoDBBsonSerialization = true,
                                    bool addCodeGeneratedAttribute = true)
    {
    }
}
";

    private static readonly DiagnosticDescriptor s_unsuportedType = new(
        id: "MFSTID0001",
        title: "Not support type",
        messageFormat: "The type '{0}' is not supported.",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new Receiver());
        context.RegisterForPostInitialization(ctx => ctx.AddSource("StronglyTypedIdAttribute.g.cs", SourceText.From(AttributeText, Encoding.UTF8)));
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var compilation = context.Compilation;

        foreach (var stronglyTypedType in GetTypes(context, compilation))
        {
            var codeUnit = new CompilationUnit
            {
                NullableContext = CodeDom.NullableContext.Enable,
            };

            var structDeclaration = CreateType(codeUnit, stronglyTypedType);
            GenerateTypeMembers(compilation, structDeclaration, stronglyTypedType);

            if (stronglyTypedType.AttributeInfo.Converters.HasFlag(StronglyTypedIdConverters.System_ComponentModel_TypeConverter))
            {
                GenerateTypeConverter(structDeclaration, compilation, stronglyTypedType.AttributeInfo.IdType);
            }

            if (stronglyTypedType.AttributeInfo.Converters.HasFlag(StronglyTypedIdConverters.System_Text_Json))
            {
                GenerateSystemTextJsonConverter(structDeclaration, compilation, stronglyTypedType);
            }

            if (stronglyTypedType.AttributeInfo.Converters.HasFlag(StronglyTypedIdConverters.Newtonsoft_Json))
            {
                GenerateNewtonsoftJsonConverter(structDeclaration, compilation, stronglyTypedType);
            }

            if (stronglyTypedType.AttributeInfo.Converters.HasFlag(StronglyTypedIdConverters.MongoDB_Bson_Serialization))
            {
                GenerateMongoDBBsonSerializationConverter(structDeclaration, compilation, stronglyTypedType);
            }

            if (stronglyTypedType.AttributeInfo.AddCodeGeneratedAttribute)
            {
                var visitor = new AddCodeGeneratedAttributeVisitor();
                visitor.Visit(codeUnit);
            }

            var result = codeUnit.ToCsharpString();
            context.AddSource(stronglyTypedType.Name + ".g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }

    private static List<StronglyTypedType> GetTypes(GeneratorExecutionContext context, Compilation compilation)
    {
        var result = new List<StronglyTypedType>();

        var receiver = (Receiver?)context.SyntaxReceiver;
        Debug.Assert(receiver != null);

        var attributeSymbol = compilation.GetTypeByMetadataName("StronglyTypedIdAttribute");
        if (attributeSymbol == null)
            return result;

        foreach (var typeDeclaration in receiver.Types)
        {
            var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
            var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken);
            Debug.Assert(symbol != null);

            var attributeInfo = GetAttributeInfo(context, semanticModel, attributeSymbol, symbol);
            if (attributeInfo == null)
                continue;

            result.Add(new(symbol.ContainingSymbol, symbol, symbol.Name, attributeInfo, typeDeclaration));
        }

        return result;
    }

    private static AttributeInfo? GetAttributeInfo(GeneratorExecutionContext context, SemanticModel semanticModel, ITypeSymbol attributeSymbol, INamedTypeSymbol declaredTypeSymbol)
    {
        foreach (var attribute in declaredTypeSymbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol))
                continue;

            var arguments = attribute.ConstructorArguments;
            if (arguments.Length != 6)
                continue;

            var idTypeArgument = arguments[0];
            if (idTypeArgument.Value is not ITypeSymbol type)
                continue;

            var converters = StronglyTypedIdConverters.None;
            AddConverter(arguments[1], StronglyTypedIdConverters.System_Text_Json);
            AddConverter(arguments[2], StronglyTypedIdConverters.Newtonsoft_Json);
            AddConverter(arguments[3], StronglyTypedIdConverters.System_ComponentModel_TypeConverter);
            AddConverter(arguments[4], StronglyTypedIdConverters.MongoDB_Bson_Serialization);
            void AddConverter(TypedConstant value, StronglyTypedIdConverters converterValue)
            {
                if (value.Value is bool argumentValue && argumentValue)
                {
                    converters |= converterValue;
                }
            }

            var addCodeGeneratedAttribute = false;
            if (arguments[5].Value is bool addCodeGeneratedAttributeValue)
            {
                addCodeGeneratedAttribute = addCodeGeneratedAttributeValue;
            }

            var idType = GetIdType(semanticModel.Compilation, type);
            if (idType != null)
                return new AttributeInfo(attribute.ApplicationSyntaxReference, idType.Value, type, converters, addCodeGeneratedAttribute);

            context.ReportDiagnostic(Diagnostic.Create(s_unsuportedType, declaredTypeSymbol.Locations.FirstOrDefault(), idTypeArgument.Type));
        }

        return null;
    }

    private static IdType? GetIdType(Compilation compilation, ITypeSymbol symbol)
    {
        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Boolean")))
            return IdType.System_Boolean;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Byte")))
            return IdType.System_Byte;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.DateTime")))
            return IdType.System_DateTime;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.DateTimeOffset")))
            return IdType.System_DateTimeOffset;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Decimal")))
            return IdType.System_Decimal;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Double")))
            return IdType.System_Double;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Guid")))
            return IdType.System_Guid;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Int16")))
            return IdType.System_Int16;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Int32")))
            return IdType.System_Int32;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Int64")))
            return IdType.System_Int64;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.SByte")))
            return IdType.System_SByte;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.Single")))
            return IdType.System_Single;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.String")))
            return IdType.System_String;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.UInt16")))
            return IdType.System_UInt16;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.UInt32")))
            return IdType.System_UInt32;

        if (SymbolEqualityComparer.Default.Equals(symbol, compilation.GetTypeByMetadataName("System.UInt64")))
            return IdType.System_UInt64;

        return null;
    }

    private static TypeReference GetTypeReference(IdType type)
    {
        return type switch
        {
            IdType.System_Boolean => new TypeReference(typeof(bool)),
            IdType.System_Byte => new TypeReference(typeof(byte)),
            IdType.System_DateTime => new TypeReference(typeof(DateTime)),
            IdType.System_DateTimeOffset => new TypeReference(typeof(DateTimeOffset)),
            IdType.System_Decimal => new TypeReference(typeof(decimal)),
            IdType.System_Double => new TypeReference(typeof(double)),
            IdType.System_Guid => new TypeReference(typeof(Guid)),
            IdType.System_Int16 => new TypeReference(typeof(short)),
            IdType.System_Int32 => new TypeReference(typeof(int)),
            IdType.System_Int64 => new TypeReference(typeof(long)),
            IdType.System_SByte => new TypeReference(typeof(sbyte)),
            IdType.System_Single => new TypeReference(typeof(float)),
            IdType.System_String => new TypeReference(typeof(string)),
            IdType.System_UInt16 => new TypeReference(typeof(ushort)),
            IdType.System_UInt32 => new TypeReference(typeof(uint)),
            IdType.System_UInt64 => new TypeReference(typeof(ulong)),
            _ => throw new ArgumentException("Type not supported", nameof(type)),
        };
    }

    private static bool IsNullable(IdType idType)
    {
        return idType == IdType.System_String;
    }

    private static string GetShortName(TypeReference typeReference)
    {
        var index = typeReference.ClrFullTypeName.LastIndexOf('.');
        return typeReference.ClrFullTypeName[(index + 1)..];
    }

    private static ClassOrStructDeclaration CreateType(CompilationUnit unit, StronglyTypedType source)
    {
        TypeDeclaration result = source switch
        {
            { IsClass: true } => new ClassDeclaration(source.Name) { Modifiers = Modifiers.Partial },
            { IsRecord: true } => new RecordDeclaration(source.Name) { Modifiers = Modifiers.Partial },
            _ => new StructDeclaration(source.Name) { Modifiers = Modifiers.Partial },
        };

        var root = result;

        var containingSymbol = source.ContainingSymbol;
        while (containingSymbol != null)
        {
            if (containingSymbol is ITypeSymbol typeSymbol)
            {
                TypeDeclaration typeDeclaration = typeSymbol.IsValueType ? new StructDeclaration() : new ClassDeclaration();
                typeDeclaration.Name = typeSymbol.Name;
                typeDeclaration.Modifiers = Modifiers.Partial;

                ((ClassOrStructDeclaration)typeDeclaration).AddType(root);
                root = typeDeclaration;
            }
            else if (containingSymbol is INamespaceSymbol nsSymbol)
            {
                var ns = GetNamespace(nsSymbol);
                if (ns == null)
                {
                    unit.AddType(root);
                }
                else
                {
                    var namespaceDeclation = new NamespaceDeclaration(ns);
                    namespaceDeclation.AddType(root);
                    unit.AddNamespace(namespaceDeclation);
                }

                break;
            }
            else
            {
                throw new InvalidOperationException($"Symbol '{containingSymbol}' of type '{containingSymbol.GetType().FullName}' not expected");
            }

            containingSymbol = containingSymbol.ContainingSymbol;
        }

        return (ClassOrStructDeclaration)result;
    }

    private static string? GetNamespace(INamespaceSymbol ns)
    {
        string? str = null;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            if (str != null)
            {
                str = '.' + str;
            }

            str = ns.Name + str;
            ns = ns.ContainingNamespace;
        }

        return str;
    }

    private static Modifiers GetPrivateOrProtectedModifier(StronglyTypedType type)
    {
        if (type.IsReferenceType && !type.IsSealed)
            return Modifiers.Protected;

        return Modifiers.Private;
    }

    private record StronglyTypedType(ISymbol ContainingSymbol, ITypeSymbol? ExistingTypeSymbol, string Name, AttributeInfo AttributeInfo, TypeDeclarationSyntax TypeDeclarationSyntax)
    {
        public bool IsClass => TypeDeclarationSyntax.IsKind(SyntaxKind.ClassDeclaration);
        public bool IsRecord => TypeDeclarationSyntax.IsKind(SyntaxKind.RecordDeclaration);
        public bool IsStruct => TypeDeclarationSyntax.IsKind(SyntaxKind.StructDeclaration);
        public bool IsReferenceType => IsClass || IsRecord;

        public bool IsSealed
        {
            get
            {
                foreach (var modifier in TypeDeclarationSyntax.Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.SealedKeyword))
                        return true;
                }

                return false;
            }
        }

        public bool IsCtorDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(".ctor").OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, AttributeInfo.IdTypeSymbol));
        }

        public bool IsFieldDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(FieldName).Any();
        }

        public bool IsValueDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(PropertyName).Any();
        }

        public bool IsValueAsStringDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(PropertyAsStringName).Any();
        }

        public bool IsToStringDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(nameof(ToString)).OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 0 && m.ReturnType?.SpecialType == SpecialType.System_String);
        }

        public bool IsGetHashcodeDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(nameof(GetHashCode)).OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 0 && m.ReturnType?.SpecialType == SpecialType.System_Int32);
        }

        public bool IsEqualsDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_Object && m.ReturnType?.SpecialType == SpecialType.System_Boolean);
        }

        public bool IsIEquatableEqualsDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers(nameof(Equals)).OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(ExistingTypeSymbol, m.Parameters[0].Type) && m.ReturnType?.SpecialType == SpecialType.System_Boolean);
        }

        public bool IsOpEqualsDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers("op_Equality").OfType<IMethodSymbol>()
                .Any(m => m.IsStatic && m.Parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(ExistingTypeSymbol, m.Parameters[0].Type) && SymbolEqualityComparer.Default.Equals(ExistingTypeSymbol, m.Parameters[1].Type) && m.ReturnType?.SpecialType == SpecialType.System_Boolean);
        }

        public bool IsOpNotEqualsDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers("op_Inequality").OfType<IMethodSymbol>()
                .Any(m => m.IsStatic && m.Parameters.Length == 2 && SymbolEqualityComparer.Default.Equals(ExistingTypeSymbol, m.Parameters[0].Type) && SymbolEqualityComparer.Default.Equals(ExistingTypeSymbol, m.Parameters[1].Type) && m.ReturnType?.SpecialType == SpecialType.System_Boolean);
        }

        public bool IsTryParseDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers("TryParse").OfType<IMethodSymbol>()
                .Any(m => m.IsStatic);
        }

        public bool IsParseDefined()
        {
            return ExistingTypeSymbol != null && ExistingTypeSymbol.GetMembers("Parse").OfType<IMethodSymbol>()
                .Any(m => m.IsStatic);
        }
    }

    private static bool IsTypeDefined(Compilation compilation, string typeMetadataName)
    {
        return compilation.References
            .Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .Select(assemblySymbol => assemblySymbol.GetTypeByMetadataName(typeMetadataName))
            .WhereNotNull()
            .Any();
    }

    private record AttributeInfo(SyntaxReference? AttributeOwner, IdType IdType, ITypeSymbol IdTypeSymbol, StronglyTypedIdConverters Converters, bool AddCodeGeneratedAttribute);

    private sealed class Receiver : ISyntaxReceiver
    {
        public List<TypeDeclarationSyntax> Types { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode.IsKind(SyntaxKind.StructDeclaration))
            {
                Types.Add((TypeDeclarationSyntax)syntaxNode);
            }
            else if (syntaxNode.IsKind(SyntaxKind.ClassDeclaration))
            {
                Types.Add((TypeDeclarationSyntax)syntaxNode);
            }
            else if (syntaxNode.IsKind(SyntaxKind.RecordDeclaration))
            {
                Types.Add((TypeDeclarationSyntax)syntaxNode);
            }
        }
    }

    [Flags]
    private enum StronglyTypedIdConverters
    {
        None = 0x0,
        System_Text_Json = 0x1,
        Newtonsoft_Json = 0x2,
        System_ComponentModel_TypeConverter = 0x4,
        MongoDB_Bson_Serialization = 0x8,
    }

    private enum IdType
    {
        System_Boolean,
        System_Byte,
        System_DateTime,
        System_DateTimeOffset,
        System_Decimal,
        System_Double,
        System_Guid,
        System_Int16,
        System_Int32,
        System_Int64,
        System_SByte,
        System_Single,
        System_String,
        System_UInt16,
        System_UInt32,
        System_UInt64,
    }
}
