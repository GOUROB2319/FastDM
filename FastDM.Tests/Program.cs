using FastDM.Tests;

BridgeAuthTests.Run();
ProtocolTests.Run();
StateStoreTests.Run();
YtDlpSitesTests.Run();
BatchImportTests.Run();
YtDlpOutputTests.Run();
FileCheckTests.Run();
SecurityRegressionTests.Run();
DownloadConcurrencyTests.Run();
YtDlpProxyTests.Run();
CookieScopeTests.Run();
YtDlpDiagnosticsTests.Run();
YtDlpWorkflowTests.Run();
HttpRangeValidationTests.Run();
await BridgeServerTests.RunAsync();
return T.Done();
