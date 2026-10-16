using SDFProject.ProgramDefinition;

namespace SDFProject.SDFProgramOperator.Helpers;

public class SDFProgramNormalizePointers
{
    public static SDFProgram Pointerify(SDFProgram program)
    {
        var mapper = new Mapper(program.pointers, program.VariableIds());

        program = program.MapRoots(mapper.Map);

        mapper.FlattenExistingEntries();

        var pointers = new SDFPointers(
            mapper.sdf.pointers,
            mapper.sdf2D.pointers,
            mapper.floats.pointers,
            mapper.integers.pointers,
            mapper.nextFreePointer);

        return program with { pointers = pointers };
    }

    private sealed class PointerTable<T>(Dictionary<string, T> existing, Func<string> allocateId, Func<string, T> createPointer)
        where T : Evaluable
    {
        public readonly Dictionary<string, T> pointers = new(existing);

        public bool Contains(string id) => pointers.ContainsKey(id);

        public T PointerTo(T node)
        {
            var id = allocateId();
            pointers[id] = node;
            return createPointer(id);
        }

        public void FlattenExisting(Mapper mapper)
        {
            foreach (var key in existing.Keys)
                pointers[key] = (T)EvaluableChildren.MapChildren(pointers[key], mapper.Map);
        }
    }

    private sealed class Mapper
    {
        public readonly PointerTable<SDFEvaluable> sdf;
        public readonly PointerTable<SDF2DEvaluable> sdf2D;
        public readonly PointerTable<FloatEvaluable> floats;
        public readonly PointerTable<IntegerEvaluable> integers;
        public long nextFreePointer;

        private readonly HashSet<string> variableIds;

        public Mapper(SDFPointers existing, HashSet<string> variableIds)
        {
            this.variableIds = variableIds;
            nextFreePointer = existing.nextFreePointer;
            sdf = new(existing.SDFEvaluablePointers, AllocateId, id => new SDFEvaluablePointerable(id));
            sdf2D = new(existing.SDF2DEvaluablePointers, AllocateId, id => new SDF2DEvaluablePointerable(id));
            floats = new(existing.FloatEvaluablePointers, AllocateId, id => new FloatEvaluablePointerable(id));
            integers = new(existing.IntegerEvaluablePointers, AllocateId, id => new IntegerEvaluablePointerable(id));
        }

        private string AllocateId() => SDFProgramRoots.AllocateId(ref nextFreePointer,
            id => sdf.Contains(id) || sdf2D.Contains(id) || floats.Contains(id) || integers.Contains(id) || variableIds.Contains(id));

        public Evaluable Map(Evaluable evaluable)
        {
            if (evaluable is Pointerable || !EvaluableChildren.HasChildren(evaluable) && evaluable is not (SDFEvaluable or SDF2DEvaluable))
                return evaluable;

            var flat = EvaluableChildren.MapChildren(evaluable, Map);

            return flat switch
            {
                SDFEvaluable e => sdf.PointerTo(e),
                SDF2DEvaluable e => sdf2D.PointerTo(e),
                FloatEvaluable e => floats.PointerTo(e),
                IntegerEvaluable e => integers.PointerTo(e),
                _ => throw new NotSupportedException($"No pointer type exists for {flat.GetType().Name}, which has child evaluables"),
            };
        }

        public void FlattenExistingEntries()
        {
            sdf.FlattenExisting(this);
            sdf2D.FlattenExisting(this);
            floats.FlattenExisting(this);
            integers.FlattenExisting(this);
        }
    }
}
