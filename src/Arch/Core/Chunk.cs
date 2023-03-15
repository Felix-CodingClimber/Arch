using System.Diagnostics.Contracts;
using Arch.Core.Extensions;
using Arch.Core.Utils;
using CommunityToolkit.HighPerformance;

namespace Arch.Core;

public unsafe struct ComponentArray
{
    public readonly IntPtr NativeArray;
    public readonly Array Array;
    public readonly int ByteSize;

    public ComponentArray(IntPtr nativeArray, int byteSize)
    {
        ByteSize = byteSize;
        NativeArray = nativeArray;
    }

    public ComponentArray(Array array, int byteSize)
    {
        ByteSize = byteSize;
        Array = array;
    }

    public static implicit operator void*(ComponentArray instance) => (void*)instance.NativeArray;
}

/// <summary>
///     The <see cref="Chunk"/> struct represents a contiguous block of memory in which various components are stored in Structure of Arrays.
///     Chunks are internally allocated and filled by <see cref="Archetype"/>'s.
///     Through them it is possible to efficiently provide or trim memory for additional entities.
/// </summary>
public unsafe partial struct Chunk
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Chunk"/> struct.
    ///     Automatically creates a lookup array for quick access to internal components.
    /// </summary>
    /// <param name="capacity">How many entities of the respective component structure fit into this <see cref="Chunk"/>.</param>
    /// <param name="types">The respective component structure of all entities in this <see cref="Chunk"/>.</param>
    internal Chunk(int capacity, params ComponentType[] types)
        : this(capacity, types.ToLookupArray(), types) { }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Chunk"/> struct
    /// </summary>
    /// <param name="capacity">How many entities of the respective component structure fit into this <see cref="Chunk"/>.</param>
    /// <param name="componentIdToArrayIndex">A lookup array which maps the component id to the array index of the component array.</param>
    /// <param name="types">The respective component structure of all entities in this <see cref="Chunk"/>.</param>
    internal Chunk(int capacity, int[] componentIdToArrayIndex, params ComponentType[] types)
    {
        // Calculate capacity and init arrays.
        Size = 0;
        Capacity = capacity;
        ComponentsSize = types.Length;

        Entities = (Entity*)Marshal.AllocHGlobal(sizeof(Entity) * Capacity);
        Components = new ComponentArray[types.Length];

        // Allocate arrays for types.
        ComponentIdToArrayIndex = componentIdToArrayIndex;
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            if (!type.IsManaged)
            {
                var ptr = Marshal.AllocHGlobal(type.ByteSize * Capacity);
                Components[index] = new ComponentArray(ptr, type.ByteSize);
            }
            else
            {
                var array = Array.CreateInstance(type, Capacity);
                Components[index] = new ComponentArray(array, type.ByteSize);
            }
        }
    }


    /// <summary>
    ///     The <see cref="Arch.Core.Entity"/>'s that are stored in this chunk.
    ///     Can be accessed during the iteration.
    /// </summary>
    public readonly Entity* Entities { [Pure] [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>
    ///     The component arrays in which the components of the <see cref="Arch.Core.Entity"/>'s are stored.
    ///     Represent the component structure.
    ///     They can be accessed quickly using the <see cref="ComponentIdToArrayIndex"/> or one of the chunk methods.
    /// </summary>
    public readonly ComponentArray[] Components { [Pure] [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>
    ///
    /// </summary>
    internal int ComponentsSize { get; set; }

    /// <summary>
    ///     The lookup array that maps component ids to component array indexes to quickly access them.
    /// </summary>
    public readonly int[] ComponentIdToArrayIndex { [Pure] [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }


    /// <summary>
    ///     The number of occupied <see cref="Arch.Core.Entity"/> slots in this <see cref="Chunk"/>.
    /// </summary>
    public int Size { [Pure] [MethodImpl(MethodImplOptions.AggressiveInlining)] get; [MethodImpl(MethodImplOptions.AggressiveInlining)] internal set; }

    /// <summary>
    ///     The number of possible <see cref="Arch.Core.Entity"/>'s in this <see cref="Chunk"/>.
    /// </summary>
    public int Capacity { [Pure] [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>
    ///     Inserts an entity into the <see cref="Chunk"/>.
    /// </summary>
    /// <param name="entity">The <see cref="Arch.Core.Entity"/> that will be inserted.</param>
    /// <returns>The index occupied by the <see cref="Arch.Core.Entity"/> in the chunk.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Add(in Entity entity)
    {
        Entities[Size] = entity;
        Size++;

        return Size - 1;
    }

    /// <summary>
    ///     Sets or replaces a component for an index in the chunk.
    /// </summary>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <param name="index">The index in the array.</param>
    /// <param name="cmp">The component value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<T>(in int index, in T cmp)
    {
        var array = GetSpan<T>();
        array[index] = cmp;
    }

    /// <summary>
    ///     Checks if a component is included in this <see cref="Chunk"/>.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>True if included, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public bool Has<T>()
    {
        var id = Component<T>.ComponentType.Id;
        return id < ComponentIdToArrayIndex.Length && ComponentIdToArrayIndex[id] != 1;
    }

    /// <summary>
    ///     Returns a component from an index within the <see cref="Chunk"/>.
    /// </summary>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <param name="index">The index.</param>
    /// <returns>A reference to the component.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public ref T Get<T>(scoped in int index)
    {
        var array = GetSpan<T>();
        return ref array[index];
    }

    /// <summary>
    ///     Returns a component at the index of the passed array.
    /// </summary>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <param name="first">The first element of the array.</param>
    /// <param name="index">The index.</param>
    /// <returns>A reference to the component.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public ref T Get<T>(ref T first, in int index)
    {
        return ref Unsafe.Add(ref first, index);
    }

    /// <summary>
    ///     Returns a component and <see cref="Arch.Core.Entity"/> from an index within the <see cref="Chunk"/>.
    /// </summary>
    /// <typeparam name="T">The generic type.</typeparam>
    /// <param name="index">The index.</param>
    /// <returns>A reference to the component.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public EntityComponents<T> GetRow<T>(scoped in int index)
    {
        var array = GetSpan<T>();
        return new EntityComponents<T>(in Entities[index], ref array[index]);
    }

    /// <summary>
    ///     Returns an <see cref="Arch.Core.Entity"/> at the index.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>A reference to the <see cref="Arch.Core.Entity"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public ref Entity Entity(scoped in int index)
    {
        return ref Entities[index];
    }

    /// <summary>
    ///     Removes the <see cref="Arch.Core.Entity"/> at an index with all its components.
    ///     Copies the last <see cref="Arch.Core.Entity"/> in its place to ensure a uniform array.
    /// </summary>
    /// <param name="index">Its index.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Remove(in int index)
    {
        // Last entity in archetype.
        var lastIndex = Size - 1;

        // Copy last entity to replace the removed one.
        Entities[index] = Entities[lastIndex];
        for (var i = 0; i < ComponentsSize; i++)
        {
            var array = Components[i];
            var indexPtr = array.NativeArray + (array.ByteSize * index);
            var lastIndexPtr = array.NativeArray + (array.ByteSize * lastIndex);
            Buffer.MemoryCopy((void*)lastIndexPtr, (void*)indexPtr, array.ByteSize, array.ByteSize);
        }

        // Update the mapping.
        Size--;
    }

    /// <summary>
    ///     Creates and returns a new <see cref="EntityEnumerator"/> instance to iterate over all used rows representing <see cref="Arch.Core.Entity"/>'s.
    /// </summary>
    /// <returns>A new <see cref="EntityEnumerator"/> instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityEnumerator GetEnumerator()
    {
        return new EntityEnumerator(Size);
    }

    /// <summary>
    ///     Cleares this <see cref="Chunk"/>, an efficient method to delete all <see cref="Arch.Core.Entity"/>s.
    ///     Does not dispose any resources nor modifies its <see cref="Capacity"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        Size = 0;
    }

    /// <summary>
    ///     Converts this <see cref="Chunk"/> to a human readable string.
    /// </summary>
    /// <returns>A string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
    {
        return $"Chunk = {{ {nameof(Capacity)} = {Capacity}, {nameof(Size)} = {Size} }}";
    }
}

public partial struct Chunk
{

    /// <summary>
    ///     Returns the component array index of a component.
    /// </summary>
    /// <typeparam name="T">The componen type.</typeparam>
    /// <returns>The index in the <see cref="Components"/> array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    private int Index<T>()
    {
        var id = Component<T>.ComponentType.Id;
        return ComponentIdToArrayIndex.DangerousGetReferenceAt(id);
    }

    /// <summary>
    ///     Returns the component array <see cref="Span{T}"/> for a given component in an unsafe manner.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The array <see cref="Span{T}"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public unsafe Span<T> GetSpan<T>()
    {
        var index = Index<T>();
        ref var array = ref Components[index];

        // Handle object components.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var arrayRef = Unsafe.As<T[]>(array.Array);
            return new Span<T>(arrayRef);
        }

        return new Span<T>((void*)array.NativeArray, Capacity);
    }

    /// <summary>
    ///     Returns a reference to the first element of a component from its component array in an unsafe manner.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>A reference to the first element.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public ref T GetFirst<T>()
    {
        return ref GetSpan<T>()[0];
    }
}

public partial struct Chunk
{

    /// <summary>
    ///     Sets or replaces a component for an index in the chunk.
    /// </summary>
    /// <param name="index">The index in the array.</param>
    /// <param name="cmp">The component value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(in int index, in object cmp)
    {
        var array = GetArray(cmp.GetType());
        array.SetValue(cmp, index);
    }

    /// <summary>
    ///     Checks if a component is included in this <see cref="Chunk"/>.
    /// </summary>
    /// <param name="t">The type.</param>
    /// <returns>True if included, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public bool Has(ComponentType t)
    {
        var id = Component.GetComponentType(t).Id;
        if (id >= ComponentIdToArrayIndex.Length)
        {
            return false;
        }

        return ComponentIdToArrayIndex.DangerousGetReferenceAt(id) != -1;
    }

    /// <summary>
    ///     Returns a component from an index within the <see cref="Chunk"/>.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="index">The index.</param>
    /// <returns>A component casted to an <see cref="object"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public object Get(scoped in int index, ComponentType type)
    {
        var array = GetArray(type);
        return array.GetValue(index);
    }

    /// <summary>
    ///     Returns the component array index of a component by its type.
    /// </summary>
    /// <param name="type">The <see cref="ComponentType"/>.</param>
    /// <returns>The index in the <see cref="Components"/> array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    private int Index(ComponentType type)
    {
        var id = type.Id;
        if (id >= ComponentIdToArrayIndex.Length)
        {
            return -1;
        }

        return ComponentIdToArrayIndex.DangerousGetReferenceAt(id);
    }

    /// <summary>
    ///      Returns the component array for a given component type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The <see cref="Array"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    public unsafe Array GetArray(ComponentType type)
    {
        var index = Index(type);
        return Unsafe.As<Array>(Components[index]);
    }
}

public partial struct Chunk
{

    /// <summary>
    ///  Copies the whole <see cref="Chunk"/> (with all its entities and components) or a part from it to the another <see cref="Chunk"/>.
    /// </summary>
    /// <param name="source">The source <see cref="Chunk"/>.</param>
    /// <param name="index">The start index in the source <see cref="Chunk"/>.</param>
    /// <param name="destination">The destination <see cref="Chunk"/>.</param>
    /// <param name="destinationIndex">The start index in the destination <see cref="Chunk"/>.</param>
    /// <param name="length">The length indicating the amount of <see cref="Entity"/>s being copied.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    internal static void Copy(ref Chunk source, int index, ref Chunk destination, int destinationIndex, int length)
    {
        /*
        // Arrays
        var entities = source.Entities;
        var sourceComponents = source.Components;

        // Copy entities array
        Array.Copy(entities, index, destination.Entities, destinationIndex, length);

        // Copy component arrays
        for (var i = 0; i < sourceComponents.Length; i++)
        {
            var sourceArray = sourceComponents[i];
            var sourceType = sourceArray.GetType().GetElementType();

            if (!destination.Has(sourceType))
            {
                continue;
            }

            var destinationArray = destination.GetArray(sourceType);
            Array.Copy(sourceArray, index, destinationArray, destinationIndex, length);
        }*/
    }

    /// <summary>
    ///     Copies an <see cref="Arch.Core.Entity"/> components at one index to another <see cref="Chunk"/>-index.
    /// </summary>
    /// <param name="source">The source <see cref="Chunk"/>.</param>
    /// <param name="index">The start index in the source <see cref="Chunk"/>.</param>
    /// <param name="destination">The destination <see cref="Chunk"/>.</param>
    /// <param name="destinationIndex">The start index in the destination <see cref="Chunk"/>.</param>
    /// <param name="length">The length indicating the amount of <see cref="Entity"/>s being copied.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    internal static void CopyComponents(ref Chunk source, int index, ref Chunk destination, int destinationIndex, int length)
    {
        /*
        // Arrays
        var sourceComponents = source.Components;

        // Copy component arrays
        for (var i = 0; i < sourceComponents.Length; i++)
        {
            var sourceArray = sourceComponents[i];
            var sourceType = sourceArray.GetType().GetElementType();

            if (!destination.Has(sourceType))
            {
                continue;
            }

            var destinationArray = destination.GetArray(sourceType);
            Array.Copy(sourceArray, index, destinationArray, destinationIndex, length);
        }*/
    }

    /// <summary>
    ///     Transfers the last <see cref="Arch.Core.Entity"/> of the referenced <see cref="Chunk"/> into this <see cref="Chunk"/> at the given index.
    /// </summary>
    /// <param name="index">The index of the <see cref="Arch.Core.Entity"/>.</param>
    /// <param name="chunk">The <see cref="Chunk"/> we want transfer the last <see cref="Arch.Core.Entity"/> from.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    internal int Transfer(int index, ref Chunk chunk)
    {
        /*
        // Get last entity
        var lastIndex = chunk.Size - 1;
        var lastEntity = chunk.Entities[lastIndex];

        // Replace index entity with the last entity from the other chunk
        Entities[index] = lastEntity;
        for (var i = 0; i < Components.Length; i++)
        {
            var sourceArray = chunk.Components[i];
            var desArray = Components[i];
            Array.Copy(sourceArray, lastIndex, desArray, index, 1);
        }

        chunk.Size--;
        return lastEntity.Id;*/
        return 0;
    }

    /*
    /// <summary>
    ///     Transfers an <see cref="Arch.Core.Entity"/> at the index of this chunk to another chunk.
    /// </summary>
    /// <param name="index">The index of the <see cref="Arch.Core.Entity"/> we want to copy.</param>
    /// <param name="chunk">The <see cref="Chunk"/> we want to transfer it to.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Pure]
    internal int CoolerTransfer(int index, ref Chunk chunk)
    {
        var chunkSize = chunk.Size;
        var chunkComponents = chunk.Components;
        var chunkEntities = chunk.Entities;
        var components = Components;
        var entities = Entities;

        // Get last entity
        var lastIndex = chunkSize - 1;
        var lastEntity = chunkEntities[lastIndex];

        // Replace index entity with the last entity from the other chunk
        entities[index] = lastEntity;
        for (var i = 0; i < components.Length; i++)
        {
            var sourceArray = chunkComponents[i];
            var desArray = components[i];
            Array.Copy(sourceArray, lastIndex, desArray, index, 1);
        }

        //chunk.Size = chunkSize - 1;
        return lastEntity.Id;
    }*/
}
