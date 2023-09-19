using System.Text;
using Arch.SourceGen;

namespace ArchSourceGenerator;

public static class ReferencesExtensions
{
    public static StringBuilder AppendComponents(this StringBuilder sb, int amount)
    {
        for (var index = 0; index < amount; index++)
            sb.AppendComponent(index);

        return sb;
    }

    public static StringBuilder AppendComponent(this StringBuilder sb, int amount)
    {

        var generics = new StringBuilder().GenericWithoutBrackets(amount);
        var parameters = new StringBuilder().GenericRefParams(amount);

        var refStructs = new StringBuilder();
        for (var index = 0; index <= amount; index++)
            refStructs.AppendLine($"public Ref<T{index}> t{index};");

        var references = new StringBuilder();
        for (var index = 0; index <= amount; index++)
            references.AppendLine($"public ref T{index} t{index};");

        var assignRefs = new StringBuilder();
        for (var index = 0; index <= amount; index++)
            assignRefs.AppendLine($"t{index} = ref t{index}Component;");


        var template =
            $$"""
            [SkipLocalsInit]
            public ref struct Components<{{generics}}>
            {
                {{references}}

                [SkipLocalsInit]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public Components({{parameters}}){

                {{assignRefs}}
                }
            }
            """;

        return sb.AppendLine(template);
    }

    public static StringBuilder AppendEntityComponents(this StringBuilder sb, int amount)
    {
        for (var index = 0; index < amount; index++)
            sb.AppendEntityComponent(index);

        return sb;
    }

    public static StringBuilder AppendEntityComponent(this StringBuilder sb, int amount)
    {

        var generics = new StringBuilder().GenericWithoutBrackets(amount);
        var parameters = new StringBuilder().GenericRefParams(amount);

        var references = new StringBuilder();
        for (var index = 0; index <= amount; index++)
            references.AppendLine($"public ref T{index} t{index};");

        var assignRefs = new StringBuilder();
        for (var index = 0; index <= amount; index++)
            assignRefs.AppendLine($"t{index} = ref t{index}Component;");


        var template =
            $$"""
            [SkipLocalsInit]
            public ref struct EntityComponents<{{generics}}>
            {
                public ref readonly Entity Entity;
                {{references}}

                [SkipLocalsInit]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public EntityComponents(ref Entity entity, {{parameters}}){

                Entity = ref entity;
                {{assignRefs}}
                }
            }
            """;

        return sb.AppendLine(template);
    }
}
