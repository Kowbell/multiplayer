using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.NetCode;

namespace Asteroids.Client
{
    /// <summary>
    /// Samples input from the player and sends it to the server. This system is also responsible
    /// for generating new PlayerSpawnRequests for this client if it doesn't have a ship entity.
    /// 
    /// It is a little convoluted how this works:
    /// Every frame we sample input as you would expect. However, rather than immediately putting
    /// that in some networkable RPC data structure, we put it in a Job. This Job is later executed
    /// by another system (presumably executing later in the frame) that calls the Job.Execute(),
    /// which actually generates the RPC data command and buffers it.
    /// 
    /// This is one of those cases where the documentation is lacking and we just kinda have to guess
    /// that this is the "right way" of doing things because that's what the Unity Gods hath ordained.
    /// </summary>
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))] // Run after the Init step of a frame but before the Presentation step
    [UpdateBefore(typeof(AsteroidsCommandSendSystem))]   // We want to send commands after they're prepped here, ofc.
    [UpdateAfter(typeof(GhostSimulationSystemGroup))]    // Not entirely sure why we update after this, but I guess we should.
    public class InputSystem : JobComponentSystem
    {
        /// <summary> Cached in OnCreate, used to schedule the input jobs </summary>
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        /// <summary> Cached in OnCreate, used to sync input ticks with the prediction system </summary>
        private GhostPredictionSystemGroup m_GhostPredict;

        /// <summary> 
        /// See OnUpdate(), we generate fake input for clients without a Presentation (i.e. rendering)
        /// system present for demonstration purposes in the sample. This counter is used to fire
        /// once every 100 frames.
        /// </summary>
        private int frameCount;

        protected override void OnCreate()
        {
            m_GhostPredict = World.GetOrCreateSystem<GhostPredictionSystemGroup>();
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        /// <summary>
        /// Sort of an intermediary class: we sample input in the InputSystem, generate one of these
        /// PlayerInputJobs with those input values, and schedule this to be processed by the
        /// "BeginSimulationEntityCommandBufferSystem" later. By "Process" I mean "Call Execute()"
        /// which means "Generate RPC command data"
        /// </summary>
        [ExcludeComponent(typeof(NetworkStreamDisconnected))]
        [RequireComponentTag(typeof(OutgoingRpcDataStreamBufferComponent))]
        struct PlayerInputJob : IJobForEachWithEntity<CommandTargetComponent>
        {
            public byte left;
            public byte right;
            public byte thrust;
            public byte shoot;
            public EntityCommandBuffer.Concurrent commandBuffer;
            public BufferFromEntity<ShipCommandData> inputFromEntity;
            public uint inputTargetTick;

            /// <summary>
            /// This will be called after input has been sampled and applied to this job data structure;
            /// more specifically, at the ScheduleSingle() call down at the bottom of OnUpdate()
            /// </summary>
            public void Execute(Entity entity, int index, [ReadOnly] ref CommandTargetComponent state)
            {
                // If this CTC doesn't have an entity, that means it is a connected client with no
                // existing ship entity. So, we spawn one!
                if (state.targetEntity == Entity.Null)
                {
                    // Spawn the player once they press the spacebar (i.e. the shoot button)
                    if (shoot != 0)
                    {
                        // Create an empty entity for the player...
                        var req = commandBuffer.CreateEntity(index);
                        // PlayerSpawnRequest is processed in SpawnSystem.cs, which is server-side.
                        commandBuffer.AddComponent<PlayerSpawnRequest>(index, req);
                        // Attach a component that can take RPC commands intended for this entity (this player)
                        commandBuffer.AddComponent(index, req, new SendRpcCommandRequestComponent {TargetConnection = entity});
                    }
                }

                // This CTC does have a ship entity, so we want to send command data for it
                else
                {
                    // If ship present, store commands in network command buffer
                    // Honestly no clue what "Exists()" means. At all. Documentation is all 
                    // auto-generated, there's no comments about this.
                    if (inputFromEntity.Exists(state.targetEntity))
                    {
                        var input = inputFromEntity[state.targetEntity];
                        input.AddCommandData(new ShipCommandData{tick = inputTargetTick, left = left, right = right, thrust = thrust, shoot = shoot});
                    }
                }
            }
        } // PlayerInputJob

        /// <summary>
        /// Each frame, this input system will sample player input, save it to a PlayerInputJob
        /// data structure, and schedule it to be processed later by the BeginSimulationEntityCommandBufferSystem
        /// </summary>
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var playerJob = new PlayerInputJob();
            playerJob.left = 0;
            playerJob.right = 0;
            playerJob.thrust = 0;
            playerJob.shoot = 0;

            // More boilerplately stuff I honestly haven't been able to wrap my head around.
            playerJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            playerJob.inputFromEntity = GetBufferFromEntity<ShipCommandData>();
            playerJob.inputTargetTick = m_GhostPredict.PredictingTick;

            // Sample keyboard inputs only if the presentation group is active,
            // aka only take input if we're rendering (not tabbed out or anything)
            if (World.GetExistingSystem<ClientPresentationSystemGroup>().Enabled)
            {
                if (Input.GetKey("left"))
                    playerJob.left = 1;
                if (Input.GetKey("right"))
                    playerJob.right = 1;
                if (Input.GetKey("up"))
                    playerJob.thrust = 1;
                //if (InputSamplerSystem.spacePresses > 0) // This is commented out in the sample project! See comments on the InputSamplerSystem.
                if (Input.GetKey("space"))
                    playerJob.shoot = 1;
            }

            // FOR THIS SAMPLE GAME (never would we do this in our own game), random inputs are
            // generated for clients that are tabbed out. This is again just for demonstration
            // purposes so there's more happening when you demo it.
            else
            {
                // Spawn and generate some random inputs
                var state = (int) Time.ElapsedTime % 3;
                if (state == 0)
                    playerJob.left = 1;
                else
                    playerJob.thrust = 1;
                ++frameCount;
                if (frameCount % 100 == 0)
                {
                    playerJob.shoot = 1;
                    frameCount = 0;
                }
            }

            // ... input has been sampled!

            var handle = playerJob.ScheduleSingle(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }
    }


    /// <summary>
    /// I believe this class was left here by accident. It's one use is to increment that spacePresses
    /// counter each frame; however, the use of that value by the InputSystem (which samples other
    /// key presses as well as sends the command data to the server) is commented out.
    /// 
    /// This class is used nowhere else in the code. I'm 99% sure this is leftover dead code.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
    public class InputSamplerSystem : ComponentSystem
    {
        public static int spacePresses;
        protected override void OnUpdate()
        {
            if (Input.GetKeyDown("space"))
                ++spacePresses;
        }
    }

    /// <summary>
    /// See also my note on the InputSamplerSystem: This class appears to have been left in by
    /// accident, or is otherwise useless.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
#if !UNITY_SERVER
    [UpdateAfter(typeof(TickClientSimulationSystem))]
#endif
    [UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
    public class InputSamplerResetSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            InputSamplerSystem.spacePresses = 0;
        }
    }
}
