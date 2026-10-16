using Xunit;

// The application keeps the SQLite path and the ProfilePics folder in static state
// (DatabaseHelper / PhotoStore), so tests must not run concurrently or they would
// point each other at the wrong database file.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
