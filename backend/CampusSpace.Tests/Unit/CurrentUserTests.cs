using System.Security.Claims;
using CampusSpace.Api.Auth;
using CampusSpace.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace CampusSpace.Tests.Unit;

public class CurrentUserTests
{
    private static CurrentUser For(HttpContext? context) => new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void Reads_sub_and_role_from_an_authenticated_request()
    {
        var identity = new ClaimsIdentity(
            [new Claim(JwtClaimNames.Sub, "42"), new Claim(JwtClaimNames.Role, Roles.Admin)], authenticationType: "Bearer");

        var user = For(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });

        user.UserId.Should().Be(42);
        user.Role.Should().Be(Roles.Admin);
        user.IsInRole(Roles.Admin).Should().BeTrue();
        user.IsInRole(Roles.Student).Should().BeFalse();
    }

    [Fact]
    public void Is_empty_outside_a_request()
    {
        var user = For(null);

        user.UserId.Should().BeNull();
        user.Role.Should().BeNull();
        user.IsInRole(Roles.Admin).Should().BeFalse();
    }

    [Fact]
    public void Is_empty_for_an_anonymous_request_even_with_claims()
    {
        var unauthenticated = new ClaimsIdentity([new Claim(JwtClaimNames.Sub, "42")]);

        var user = For(new DefaultHttpContext { User = new ClaimsPrincipal(unauthenticated) });

        user.UserId.Should().BeNull();
    }
}
