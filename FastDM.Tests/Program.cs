using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
StateStoreTests.Run();
YtDlpSitesTests.Run();
BatchImportTests.Run();
YtDlpOutputTests.Run();
FileCheckTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
