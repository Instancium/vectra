$env:VECTRA_TEST_CORPUS_PDF = "C:\corpus\Lessons-In-Industrial-Instrumentation.pdf"
dotnet test ../Instancium.Vectra.Tests.Integration --filter "Category=Integration"