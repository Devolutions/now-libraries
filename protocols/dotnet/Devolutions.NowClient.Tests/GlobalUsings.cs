global using Devolutions.NowProto;
global using Devolutions.NowProto.Capabilities;
global using Devolutions.NowProto.Messages;

global using Xunit;

global using Client = Devolutions.NowClient.NowClient;

// ThreadPool limits are process-wide. No test in this assembly may overlap the
// starvation test; each target framework runs in its own testhost process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]