using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
StateStoreTests.Run();
YtDlpSitesTests.Run();
BatchImportTests.Run();
YtDlpOutputTests.Run();
FileCheckTests.Run();
SecurityRegressionTests.Run();
YtDlpProxyTests.Run();
CookieScopeTests.Run();
YtDlpDiagnosticsTests.Run();
HttpRangeValidationTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
