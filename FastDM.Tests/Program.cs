using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
StateStoreTests.Run();
YtDlpSitesTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
