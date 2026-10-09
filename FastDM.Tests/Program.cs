using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
