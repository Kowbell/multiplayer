using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

/// <summary>
/// Server-side system which takes the buffered player inputs and applies them to the controlled cube.
/// 
/// See CubeInput.cs for the client-side system which generates and buffers those player inputs.
/// </summary>
[UpdateInGroup(typeof(GhostPredictionSystemGroup))]
public class MoveCubeSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        var group = World.GetExistingSystem<GhostPredictionSystemGroup>();
        var tick = group.PredictingTick;
        var deltaTime = Time.DeltaTime;

        // Get all of the controlled cube entities in the world, all of which have a CubeInput buffer
        // as well as a translation (i.e. position) and a prediction component which will magically
        // determine if we should apply this input or if there's some crazy prediction stuff going on.
        Entities.ForEach((DynamicBuffer<CubeInput> inputBuffer, ref Translation trans, ref PredictedGhostComponent prediction) =>
        {
            if (!GhostPredictionSystemGroup.ShouldPredict(tick, prediction))
                return;

            // Get the input buffer data
            CubeInput input;
            inputBuffer.GetDataAtTick(tick, out input);

            // Apply the input 
            if (input.horizontal > 0)
                trans.Value.x += deltaTime;
            if (input.horizontal < 0)
                trans.Value.x -= deltaTime;
            if (input.vertical > 0)
                trans.Value.z += deltaTime;
            if (input.vertical < 0)
                trans.Value.z -= deltaTime;
        });
    }
}

