namespace BrassLedger.Web.E2E.Tests;

[CollectionDefinition("Playwright E2E", DisableParallelization = true)]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightWebAppFixture>
{
}

[CollectionDefinition("Playwright E2E Mutable", DisableParallelization = true)]
public sealed class PlaywrightMutableCollection : ICollectionFixture<PlaywrightWebAppFixture>
{
}

[CollectionDefinition("Playwright E2E Visual", DisableParallelization = true)]
public sealed class PlaywrightVisualCollection : ICollectionFixture<PlaywrightWebAppFixture>
{
}
