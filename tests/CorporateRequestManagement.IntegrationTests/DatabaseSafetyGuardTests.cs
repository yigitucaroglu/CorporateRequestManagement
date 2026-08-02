namespace CorporateRequestManagement.IntegrationTests;

public sealed class DatabaseSafetyGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Production")]
    [InlineData("CorporateRequestManagement_IntegrationTests_")]
    [InlineData("CorporateRequestManagement_IntegrationTests_not-a-guid")]
    [InlineData("CorporateRequestManagement_IntegrationTests_00000000000000000000000000000000_extra")]
    public void ValidateDatabaseName_WhenNameIsUnsafe_ShouldReject(string? databaseName)
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerDatabaseFixture.ValidateDatabaseName(databaseName));
    }

    [Fact]
    public void ValidateDatabaseName_WhenGuidSuffixIsValid_ShouldAcceptCaseInsensitivePrefix()
    {
        string name = "corporaterequestmanagement_integrationtests_" + Guid.NewGuid().ToString("N");

        SqlServerDatabaseFixture.ValidateDatabaseName(name);
    }
}
