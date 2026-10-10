using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
StateStoreTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
