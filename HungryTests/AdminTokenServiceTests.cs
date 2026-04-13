using System.Collections.Generic;
using FluentAssertions;
using HungryGame;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class AdminTokenServiceTests
{
    private static AdminTokenService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SECRET_CODE"] = "swordfish"
            })
            .Build();

        return new AdminTokenService(config);
    }

    [Test]
    public void Login_ReturnsNullForIncorrectPassword()
    {
        var service = CreateService();

        service.Login("wrong-password").Should().BeNull();
    }

    [Test]
    public void Login_ReturnsTrackableTokenForCorrectPassword()
    {
        var service = CreateService();

        var token = service.Login("swordfish");

        token.Should().NotBeNullOrWhiteSpace();
        service.IsValid(token).Should().BeTrue();
    }

    [Test]
    public void Logout_InvalidatesExistingToken()
    {
        var service = CreateService();
        var token = service.Login("swordfish");

        service.Logout(token!);

        service.IsValid(token).Should().BeFalse();
    }
}
