using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using DseCecil;
using DseCecil.Cil;

public static class NativeApiIndex
{
    public static void Build(string assemblyPath, string steamBuildId, string destination)
    {
        byte[] bytes = File.ReadAllBytes(assemblyPath);
        using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        var module = assembly.MainModule;
        var symbols = new List<object>();
        var references = new List<object>();

        foreach (var type in module.GetTypes())
        {
            symbols.Add(new
            {
                id = type.FullName, name = type.Name, kind = "type",
                access = type.Attributes & TypeAttributes.VisibilityMask,
                base_type = type.BaseType?.FullName,
                interfaces = type.Interfaces.Select(item => item.InterfaceType.FullName).ToArray()
            });
            foreach (var field in type.Fields)
            {
                symbols.Add(new
                {
                    id = field.FullName, name = field.Name, kind = "field", owner = type.FullName,
                    access = field.Attributes & FieldAttributes.FieldAccessMask, is_static = field.IsStatic
                });
            }
            foreach (var property in type.Properties)
            {
                symbols.Add(new
                {
                    id = property.FullName, name = property.Name, kind = "property", owner = type.FullName,
                    getter = property.GetMethod?.FullName, setter = property.SetMethod?.FullName
                });
            }
            foreach (var method in type.Methods)
            {
                symbols.Add(new
                {
                    id = method.FullName, name = method.Name, kind = "method", owner = type.FullName,
                    access = method.Attributes & MethodAttributes.MemberAccessMask, is_static = method.IsStatic,
                    parameters = method.Parameters.Select(item => new { name = item.Name, type = item.ParameterType.FullName }).ToArray(),
                    generic_parameters = method.GenericParameters.Select(item => item.Name).ToArray()
                });
                if (!method.HasBody)
                    continue;

                var seen = new HashSet<(string, string, string)>();
                foreach (var instruction in method.Body.Instructions)
                {
                    string operation;
                    MemberReference target;
                    switch (instruction.OpCode.Code)
                    {
                        case Code.Call:
                        case Code.Callvirt:
                            operation = "call";
                            target = (MethodReference)instruction.Operand;
                            break;
                        case Code.Newobj:
                            operation = "construct";
                            target = (MethodReference)instruction.Operand;
                            break;
                        case Code.Ldftn:
                        case Code.Ldvirtftn:
                            operation = "method_pointer";
                            target = (MethodReference)instruction.Operand;
                            break;
                        case Code.Ldfld:
                        case Code.Ldsfld:
                            operation = "read_field";
                            target = (FieldReference)instruction.Operand;
                            break;
                        case Code.Stfld:
                        case Code.Stsfld:
                            operation = "write_field";
                            target = (FieldReference)instruction.Operand;
                            break;
                        case Code.Ldflda:
                        case Code.Ldsflda:
                            operation = "field_address";
                            target = (FieldReference)instruction.Operand;
                            break;
                        default:
                            continue;
                    }
                    // Resolve local generic instances to the same signature as their definition.
                    bool local = target.DeclaringType.Scope == module;
                    string scope = local ? assembly.Name.Name : target.DeclaringType.Scope.Name;
                    string targetId = target.FullName;
                    if (local && target is MethodReference targetMethod)
                        targetId = targetMethod.Resolve()?.FullName ?? targetId;
                    if (local && target is FieldReference targetField)
                        targetId = targetField.Resolve()?.FullName ?? targetId;
                    if (seen.Add((scope, targetId, operation)))
                        references.Add(new { source = method.FullName, target = targetId, assembly = scope, operation });
                }
            }
        }

        using var stream = File.Create(destination);
        JsonSerializer.Serialize(stream, new
        {
            schema = 1,
            source = new
            {
                path = assemblyPath,
                name = assembly.Name.Name,
                version = assembly.Name.Version.ToString(),
                mvid = module.Mvid,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                steam_build_id = steamBuildId.Length == 0 ? null : steamBuildId
            },
            symbols,
            references
        }, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
    }
}
