using System.CodeDom;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Build.Construction;
using Microsoft.CSharp;

namespace CsFakeGenerator
{
    internal static class Program
    {
        private static int indentLevel = 0;
        private static string Indent => new string(' ', indentLevel * 4);
        private static List<FileInfo> generatedCsFiles = new();
        
        private static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: CsFakeGenerator.exe [input dll file] [output path]");
                return;
            }

            var inputFile = args[0];
            var outputFolder = args[1];

            if (!File.Exists(inputFile))
            {
                Console.WriteLine($"Input file [{args[0]}] does not exist.");
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Console.WriteLine($"Output directory [{args[1]}] does not exist. Create it? Y/N ");
                var key = Console.ReadKey();
                if (key.Key != ConsoleKey.Y)
                {
                    return;
                }
            }
            
            ProcessAssembly(inputFile, outputFolder);
        }

        /// <summary>
        /// Iterates through all public types in the specified assembly and outputs fakes of these types to the output folder.
        /// </summary>
        /// <param name="inputFile">The DLL file to process.</param>
        /// <param name="outputFolder">The folder in which to save the fakes.</param>
        /// <exception cref="ArgumentException">The input file does not exist.</exception>
        private static void ProcessAssembly(string inputFile, string outputFolder)
        {
            if (!File.Exists(inputFile)) throw new ArgumentException("Input file does not exist", nameof(inputFile));

            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            
            var assembly = Assembly.LoadFrom(inputFile);
            outputFolder = new DirectoryInfo(outputFolder).FullName;

            foreach (var type in assembly.ExportedTypes)
            {
                // Skip nested types, they'll be picked up by their parent types
                if (!type.IsNested)
                {
                    indentLevel = 0;
                    var filePath = outputFolder + Path.DirectorySeparatorChar + GetTypeName(type, false) + ".cs";
                    File.WriteAllText(filePath, ProcessType(type));
                    generatedCsFiles.Add(new FileInfo(filePath));
                }
            }

            CreateProjectFile(assembly, inputFile, outputFolder);
        }

        /// <summary>
        /// Saves a csproj file in the output folder to build the fake library.
        /// </summary>
        /// <param name="assembly">The input assembly. Used to copy across references.</param>
        /// <param name="inputFile">The location of the input file (assembly).</param>
        /// <param name="outputFolder">The output folder.</param>
        private static void CreateProjectFile(Assembly assembly, string inputFile, string outputFolder)
        {
            var projectRoot = ProjectRootElement.Create();
            var propertyGroup = projectRoot.AddPropertyGroup();
            propertyGroup.AddProperty("TargetFramework", "netstandard2.0");
            propertyGroup.AddProperty("Configuration", "Debug");
            propertyGroup.AddProperty("Platform", "x64");

            var referenceGroup = projectRoot.AddItemGroup();
            var inputFolder = inputFile.Substring(0, inputFile.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var item = referenceGroup.AddItem("Reference", reference.FullName);
            }

            var compileGroup = projectRoot.AddItemGroup();
            foreach (var generatedCsFile in generatedCsFiles)
            {
                compileGroup.AddItem("Compile", generatedCsFile.Name);
            }

            var target = projectRoot.AddTarget("Build");
            var task = target.AddTask("Csc");
            task.SetParameter("Sources", "@(Compile)");
            task.SetParameter("OutputAssembly", new FileInfo(inputFile).Name);
            
            projectRoot.Save(outputFolder + Path.DirectorySeparatorChar + new FileInfo(inputFile).Name + ".csproj");
        }

        /// <summary>
        /// Creates a fake for a type.
        /// </summary>
        /// <param name="type">The type to make a fake for, e.g. a class, enum, interface, struct.</param>
        /// <returns>A string with a fake for this type.</returns>
        private static string ProcessType(Type type)
        {
            bool typeHasNamespace = type.Namespace != null && !type.IsNested;
            var typeOutput = new StringBuilder();

            // Namespace declaration
            if (typeHasNamespace)
            {
                typeOutput.Append("namespace ").Append(type.Namespace).Append("\n{\n");
                indentLevel++;
            }

            // Type declaration
            typeOutput.Append(Indent).Append(GetTypeDeclaration(type)).Append('\n');
            typeOutput.Append(Indent).Append("{\n");
            indentLevel++;

            // Add nested types
            foreach (var nestedType in type.GetNestedTypes())
            {
                typeOutput.Append(ProcessType(nestedType));
                typeOutput.Append('\n');
            }

            // Add fields to classes / structs / interfaces
            if (!type.IsEnum)
            {
                foreach (var field in type.GetFields())
                {
                    typeOutput.Append(ProcessField(field));
                }
                typeOutput.Append('\n');
            }

            // Add enum members
            if (type.IsEnum)
            {
                typeOutput.Append(ProcessEnumValues(type));
                typeOutput.Append('\n');
            }

            // Add properties
            foreach (var property in type.GetProperties())
            {
                typeOutput.Append(ProcessProperty(property));
            }
            typeOutput.Append('\n');

            // Add methods
            if (!type.IsEnum)
            {
                foreach (var method in type.GetMethods())
                {
                    typeOutput.Append(ProcessMethod(method));
                }

                typeOutput.Append('\n');
            }

            indentLevel--;
            typeOutput.Append(Indent).Append("}\n"); // Close class brace
            if (typeHasNamespace)
            {
                indentLevel--;
                typeOutput.Append("}\n"); // Close namespace braces
            }

            return typeOutput.ToString();
        }

        /// <summary>
        /// Lists out the enum's fields with their values.
        /// </summary>
        /// <param name="type">An enum type</param>
        /// <returns>A string with all fields in the enum.</returns>
        private static string ProcessEnumValues(Type type)
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            var enumValues = new List<string>();

            foreach (var value in Enum.GetValues(type))
            {
                enumValues.Add(string.Concat(Indent, value, " = ", ((int) value).ToString()));
            }

            return string.Join(",\n", enumValues);
        }

        /// <summary>
        /// Creates a line declaring a field.
        /// </summary>
        /// <param name="fieldInfo">The field to create a declaration for.</param>
        /// <returns>A string declaring the field, or an empty string if the field is inherited from a base type.</returns>
        private static string ProcessField(FieldInfo fieldInfo)
        {
            if (IsInherited(fieldInfo)) return string.Empty;
            
            var fieldDeclaration = new StringBuilder(Indent);
            fieldDeclaration.Append(GetAccessModifier(fieldInfo));

            // Add static modifier. If the parent class is static and this member is not, skip it.
            if (fieldInfo.IsStatic) fieldDeclaration.Append("static ");
            if (fieldInfo.ReflectedType != null && fieldInfo.ReflectedType.IsAbstract &&
                fieldInfo.ReflectedType.IsSealed && !fieldInfo.IsStatic) return string.Empty;

            // Field type and name
            fieldDeclaration.Append(GetTypeName(fieldInfo.FieldType, true)).Append(' ').Append(fieldInfo.Name).Append(";\n");
            
            return fieldDeclaration.ToString();
        }
        
        /// <summary>
        /// Creates a line declaring a property.
        /// </summary>
        /// <param name="propertyInfo">The property to create a declaration for.</param>
        /// <returns>A string declaring the specified property e.g. public [type] [name] { get; set; }</returns>
        private static string ProcessProperty(PropertyInfo propertyInfo)
        {
            var propertyMethod = propertyInfo.GetMethod ?? propertyInfo.SetMethod;
            if (propertyMethod == null) return string.Empty;

            if (IsInherited(propertyMethod)) return string.Empty;

            var propertyDeclaration = new StringBuilder(Indent);
            propertyDeclaration.Append(GetAccessModifier(propertyInfo));
            propertyDeclaration.Append(GetOverrideModifier(propertyMethod));

            // Add static modifier. If the parent class is static and this member is not, skip it.
            if (propertyMethod.IsStatic) propertyDeclaration.Append("static ");
            if (propertyMethod.ReflectedType != null && propertyMethod.ReflectedType.IsAbstract &&
                propertyMethod.ReflectedType.IsSealed && !propertyMethod.IsStatic) return string.Empty;

            // Property type and generic arguments
            propertyDeclaration.Append(GetTypeName(propertyInfo.PropertyType, true));
            propertyDeclaration.Append(GetGenericTypeArguments(propertyInfo.PropertyType, true));
            
            // Property name
            propertyDeclaration.Append(' ').Append(propertyInfo.Name).Append(" { ");

            // Getter / setter
            if (propertyInfo.CanRead) propertyDeclaration.Append("get; ");
            if (propertyInfo.CanWrite) propertyDeclaration.Append("set; ");

            // Close property brace
            propertyDeclaration.Append("}\n");

            return propertyDeclaration.ToString();
        }

        /// <summary>
        /// Returns an appropriate access modifier for the specified member, based on its parent type.
        /// </summary>
        /// <param name="memberInfo">The member.</param>
        /// <returns>"public " when the member is not on an interface, otherwise an empty string.</returns>
        private static string GetAccessModifier(MemberInfo memberInfo)
        {
            if (memberInfo.DeclaringType != null && !memberInfo.DeclaringType.IsInterface) return "public ";

            return string.Empty;
        }
        
        /// <summary>
        /// Creates a method declaration with default return value.
        /// </summary>
        /// <param name="methodInfo">The method to create a declaration for.</param>
        /// <returns>A string containing the method's declaration and a method body returning a default value.</returns>
        private static string ProcessMethod(MethodInfo methodInfo)
        {
            // Return nothing if inherited and not overridden
            if (IsInherited(methodInfo)) return string.Empty;

            var methodIsOnInterface = methodInfo.DeclaringType != null && methodInfo.DeclaringType.IsInterface;
            
            // Return nothing if this method is part of a Property
            if (methodInfo.Name.Length > 4 && methodInfo.DeclaringType != null && 
                methodInfo.DeclaringType.GetProperty(methodInfo.Name.Substring(4)) != null)
            {
                return string.Empty;
            }

            var methodDeclaration = new StringBuilder(Indent);
            methodDeclaration.Append(GetAccessModifier(methodInfo));
            methodDeclaration.Append(GetOverrideModifier(methodInfo));

            // Add static modifier. If the parent class is static and this member is not, skip it.
            if (methodInfo.IsStatic) methodDeclaration.Append("static ");
            if (methodInfo.ReflectedType != null && methodInfo.ReflectedType.IsAbstract &&
                methodInfo.ReflectedType.IsSealed && !methodInfo.IsStatic) return string.Empty;

            if (methodInfo.ReturnType == typeof(void))
            {
                methodDeclaration.Append("void");
            }
            else
            {
                methodDeclaration.Append(GetTypeName(methodInfo.ReturnType, true));
                methodDeclaration.Append(GetGenericTypeArguments(methodInfo.ReturnType, true));
            }

            // Name, parameters
            methodDeclaration.Append(' ').Append(methodInfo.Name);
            methodDeclaration.Append(GetGenericTypeArguments(methodInfo, true));
            methodDeclaration.Append(GetMethodParameters(methodInfo));
            
            if (methodIsOnInterface || methodInfo.IsAbstract)
            {
                // If this is on an interface or abstract, don't create a method body
                methodDeclaration.Append(";\n");
                return methodDeclaration.ToString();
            }

            // Method opening brace
            methodDeclaration.Append('\n').Append(Indent).Append("{\n");
            
            // Return default value
            indentLevel++;
            if (methodInfo.ReturnType == typeof(void))
            {
                methodDeclaration.Append(Indent).Append("return;\n");
            }
            else if (methodInfo.ReturnType.IsValueType || methodInfo.IsGenericMethod)
            {
                methodDeclaration.Append(Indent).Append("return default;\n");
            }
            else
            {
                methodDeclaration.Append(Indent).Append("return null;\n");
            }

            indentLevel--;
            
            // Close method
            methodDeclaration.Append(Indent).Append("}\n\n");

            return methodDeclaration.ToString();
        }

        private static string GetOverrideModifier(MethodInfo methodInfo)
        {
            // Not required on an interface
            if (methodInfo.DeclaringType != null && methodInfo.DeclaringType.IsInterface) return string.Empty;
            
            if (methodInfo.IsAbstract)
            {
                // Property is an abstract member
                return "abstract ";
            }

            if (methodInfo.GetBaseDefinition().IsAbstract)
            {
                // Override abstract base class member
                return "override ";
            }

            if (methodInfo.DeclaringType != methodInfo.ReflectedType)
            {
                // Member is declared on another type (i.e. inherited)
                if ((methodInfo.Attributes & MethodAttributes.Final) == 0 && !methodInfo.GetBaseDefinition().IsVirtual)
                {
                    // Overrides are flagged as final
                    return "override ";
                }

                // If not final, it's hiding the base method
                return "new ";
            }

            return string.Empty;
        }

        /// <summary>
        /// Creates a line declaring the passed in type.
        /// </summary>
        /// <param name="type">A type declared in the input assembly.</param>
        /// <returns>A string declaring the type.</returns>
        private static string GetTypeDeclaration(Type type)
        {
            var typeDeclaration = new StringBuilder("public ");

            if (type.IsClass)
            {
                // Class modifiers
                if (type.IsAbstract && type.IsSealed) typeDeclaration.Append("static ");
                else if (type.IsAbstract) typeDeclaration.Append("abstract ");
                else if (type.IsSealed) typeDeclaration.Append("sealed ");

                typeDeclaration.Append("class ");
            }

            if (type.IsInterface) typeDeclaration.Append("interface ");
            if (type.IsEnum) typeDeclaration.Append("enum ");
            if (!type.IsEnum && type.IsValueType) typeDeclaration.Append("struct ");

            // Add name of type with any generic arguments
            typeDeclaration.Append(GetTypeName(type, false) + GetGenericTypeArguments(type, false) + GetGenericTypeArgumentRestrictions(type));

            // Do not add interfaces or inheritance on enums
            if (type.IsEnum) return typeDeclaration.ToString();

            var inherits = new List<string>();
            if (type.BaseType != null && type.BaseType != typeof(Object) && type.BaseType != typeof(Enum) && type.BaseType != typeof(ValueType))
            {
                // Add base type if not a primitive
                inherits.Add(GetTypeName(type.BaseType, true) + GetGenericTypeArguments(type.BaseType, true));
            }
            
            // Add interfaces
            Array.ForEach<Type>(type.GetInterfaces(), i => inherits.Add(GetTypeName(i, true) + GetGenericTypeArguments(i, true)));

            // If a base class and/or interfaces were found, add them to the type declaration
            if (inherits.Count > 0) typeDeclaration.Append(" : ").Append(string.Join(", ", inherits));

            return typeDeclaration.ToString();
        }

        /// <summary>
        /// Gets the type name to be used in various settings, such as file names, type declarations, parameters, etc.
        /// </summary>
        /// <param name="type">The type to give the name for.</param>
        /// <param name="fullName">Whether to fully qualify the name with its namespace.</param>
        /// <returns>The name, fully qualified or not, with generic markers removed and corrected for nested classes.</returns>
        private static string GetTypeName(Type type, bool fullName)
        {
            var typeName = fullName ? type.FullName ?? type.Name : type.Name;
            if (typeName.Contains('`')) typeName = typeName.Substring(0, typeName.IndexOf("`", StringComparison.Ordinal)); // Remove `1 from generic types
            if (typeName.Contains('+')) typeName = typeName.Replace('+', '.'); // Replace + from nested types
            
            return typeName;
        }

        /// <summary>
        /// Creates a list of method parameters in brackets.
        /// </summary>
        /// <param name="methodInfo">The method to get the parameters for.</param>
        /// <returns>A string containing method parameter declarations in brackets.</returns>
        private static string GetMethodParameters(MethodInfo methodInfo)
        {
            var parameters = new List<string>();
            foreach (var parameterInfo in methodInfo.GetParameters())
            {
                parameters.Add(string.Concat(GetTypeName(parameterInfo.ParameterType, true),
                    GetGenericTypeArguments(parameterInfo.ParameterType, true),
                    " ", parameterInfo.Name));
            }

            return string.Concat("(", string.Join(", ", parameters), ")");
        }
        /// <summary>
        /// If the type takes generic type arguments (e.g. generic List T) then this method returns the generic arguments enclosed in &lt; and &gt;
        /// </summary>
        /// <param name="type">The type to return generic arguments for.</param>
        /// <param name="useTypeNames">If true, the types are returned e.g. string, otherwise the name of the generic parameter is returned e.g. T.</param>
        /// <returns>The generic arguments of the specified type, or an empty string if this type does not take generic arguments.</returns>
        private static string GetGenericTypeArguments(Type type, bool useTypeNames)
        {
            if (!type.IsGenericType) return string.Empty;
            
            var typeDefinition = useTypeNames ? type : type.GetGenericTypeDefinition();
            var genericArgumentNames = new List<string>();

            foreach (var genericArgument in typeDefinition.GetGenericArguments())
            {
                genericArgumentNames.Add(GetTypeName(genericArgument, true));
            }

            return string.Concat("<", string.Join(", ", genericArgumentNames), ">");
        }

        private static string GetGenericTypeArguments(MethodInfo methodInfo, bool useTypeNames)
        {
            if (!methodInfo.IsGenericMethod) return string.Empty;

            var memberDefinition = useTypeNames ? methodInfo : methodInfo.GetGenericMethodDefinition();
            var genericArgumentNames = new List<string>();

            foreach (var genericArgument in methodInfo.GetGenericArguments())
            {
                genericArgumentNames.Add(GetTypeName(genericArgument, true));
            }
            
            return string.Concat("<", string.Join(", ", genericArgumentNames), ">");
        }

        /// <summary>
        /// If the type takes generic type arguments with restrictions (e.g. Class T where T : OtherClass), this returns the type restrictions (where clause).
        /// </summary>
        /// <param name="type">The type to return the generic argument restrictions for.</param>
        /// <returns>A list of type restrictions, or an empty string if the type does not have any.</returns>
        private static string GetGenericTypeArgumentRestrictions(Type type)
        {
            if (!type.IsGenericType) return string.Empty;

            var restrictions = new StringBuilder();
            
            // Loop through all generic arguments
            var typeDefinition = type.GetGenericTypeDefinition();
            foreach (var genericArgument in typeDefinition.GetGenericArguments())
            {
                // For each argument, loop through its restrictions
                var argumentRestrictions = genericArgument.GetGenericParameterConstraints();
                if (argumentRestrictions.Length > 0)
                {
                    // Append all restrictions for this type
                    restrictions.Append(" where ").Append(genericArgument.Name).Append(" : ");
                    var argumentRestrictionsList = new List<string>();
                    for (int i = 0; i < argumentRestrictions.Length; i++)
                    {
                        if (argumentRestrictions[i] == typeof(System.ValueType) && !argumentRestrictions[i].IsEnum)
                        {
                            // ValueType that isn't an enum is a struct
                            argumentRestrictionsList.Insert(0, "struct");
                        }
                        else
                        {
                            // Otherwise get the name
                            argumentRestrictionsList.Add(GetTypeName(argumentRestrictions[i], true));
                        }
                    }
                    restrictions.Append(string.Join(", ", argumentRestrictionsList));
                }
            }

            return restrictions.ToString();
        }

        /// <summary>
        /// Checks whether the member is inherited without override.
        /// </summary>
        /// <param name="memberInfo">The member (property, method) to check.</param>
        /// <returns>True if the member is inherited without overriding, false if the member isn't inherited,
        /// or is inherited but overridden in this class.</returns>
        private static bool IsInherited(MethodInfo memberInfo)
        {
            // If this isn't the declaring type, it's inherited
            if (memberInfo.DeclaringType != memberInfo.ReflectedType)
            {
                if ((memberInfo.Attributes & MethodAttributes.NewSlot) == 0)
                {
                    // If the method isn't virtual and doesn't take up a new slot, meaning it's inherited, not override
                    return true;
                }
                if ((memberInfo.Name.Equals("get_TypeId") || memberInfo.Name.Equals("set_TypeId"))
                    && memberInfo.ReturnType == typeof(Object))
                {
                    // Special case, this is always implemented on each object so ignore it
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Checks whether the field is declared in the current type.
        /// </summary>
        /// <param name="memberInfo">The field's FieldInfo.</param>
        /// <returns>True if the field is inherited, false if it is declared on the current type.</returns>
        private static bool IsInherited(FieldInfo memberInfo)
        {
            // If this isn't the declaring type, it's inherited
            return memberInfo.DeclaringType != memberInfo.ReflectedType;
        }

    }
}