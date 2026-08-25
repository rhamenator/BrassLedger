using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BrassLedger.Application.Accounting;
using BrassLedger.Domain.Accounting;
using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.SecurityAdministration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BrassLedger.Api.Tests;

public sealed class ApiIntegrationTests : IClassFixture<BrassLedgerApiFactory>
{
    private readonly BrassLedgerApiFactory _factory;

    public ApiIntegrationTests(BrassLedgerApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDashboard_RejectsAnonymousRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_ReturnsSeededFinancialSnapshot()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(dashboard);
        Assert.Equal(112540.32m, dashboard.CashOnHand);
        Assert.Equal(34715.75m, dashboard.ReceivablesOpen);
        Assert.Equal(31844.77m, dashboard.PayablesOpen);
        Assert.Equal(14, dashboard.EnabledModules);
    }

    [Fact]
    public async Task GetWorkspace_ReturnsModulesAndReportingCatalog()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");

        Assert.NotNull(workspace);
        Assert.Equal("Brass Ledger Manufacturing", workspace.Company.Name);
        Assert.Contains(workspace.Modules, module => module.Code == "J" && module.Status == "Live foundation");
        Assert.Contains(workspace.Reporting.Reports, report => report.Code == "RDL-GL-TRIAL");
        Assert.Contains(workspace.Taxes.Profiles, profile => profile.Jurisdiction == "Federal" && profile.TaxType == "FUTA");
    }

    [Fact]
    public async Task ApiLogin_LocksOperatorAfterRepeatedFailures()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.MaxFailedSignInAttempts - 1; attempt++)
        {
            var failedResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                UserName = "controller",
                Password = "wrong-password"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        var lockedResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Locked, lockedResponse.StatusCode);
    }

    [Fact]
    public async Task ExistingSession_IsRejectedAfterSecurityStampChanges()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var user = await dbContext.Users.SingleAsync(x => x.UserName == "controller");
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ReissuesCurrentSession_RevokesOtherSessions_AndAuditsTheChange()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var currentClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var otherClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        var token = await GetAntiforgeryTokenAsync(currentClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/change-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["currentPassword"] = BrassLedgerAuthenticationDefaults.SeededPassword,
                ["newPassword"] = "Changed password! 2026",
                ["confirmPassword"] = "Changed password! 2026"
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await currentClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/security?status=password-changed", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await currentClient.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/dashboard")).StatusCode);

        using var oldPasswordClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var oldPasswordResponse = await oldPasswordClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        using var newPasswordClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var newPasswordResponse = await newPasswordClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "Changed password! 2026"
        });
        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.True(await dbContext.AuthenticationAuditEntries.AnyAsync(entry =>
            entry.UserName == "controller" && entry.EventType == "password_changed" && entry.Succeeded));
    }

    [Fact]
    public async Task ChangePassword_RejectsInvalidCurrentPassword_AndMissingAntiforgeryToken()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var missingTokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["currentPassword"] = BrassLedgerAuthenticationDefaults.SeededPassword,
            ["newPassword"] = "Changed password! 2026",
            ["confirmPassword"] = "Changed password! 2026"
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/change-password", missingTokenContent)).StatusCode);

        var token = await GetAntiforgeryTokenAsync(client);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/account/change-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["currentPassword"] = "not-the-current-password",
                ["newPassword"] = "Changed password! 2026",
                ["confirmPassword"] = "Changed password! 2026"
            })
        };
        invalidRequest.Headers.Add("X-CSRF-TOKEN", token);
        var invalidResponse = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.Redirect, invalidResponse.StatusCode);
        Assert.Equal("/account/security?error=current-password", invalidResponse.Headers.Location?.OriginalString);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        Assert.True(await dbContext.AuthenticationAuditEntries.AnyAsync(entry =>
            entry.UserName == "controller" && entry.EventType == "password_change_failed" && !entry.Succeeded));
    }

    [Fact]
    public async Task RevokeOtherSessions_KeepsCurrentBrowserSignedIn()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var currentClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        using var otherClient = await CreateAuthenticatedClientAsync(isolatedFactory);
        var token = await GetAntiforgeryTokenAsync(currentClient);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/revoke-other-sessions");
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await currentClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await currentClient.GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/dashboard")).StatusCode);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        var sessions = await db.UserSessions.Where(session => session.UserId == user.Id).ToListAsync();
        Assert.Single(sessions, session => session.RevokedAtUtc is null && session.SecurityStamp == user.SecurityStamp);
        Assert.Equal(2, sessions.Count(session => session.RevokedAtUtc is not null));
    }

    [Fact]
    public async Task Logout_RevokesCurrentNamedSessionAndRecordsCompanyAudit()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(candidate => candidate.UserName == "controller");
        var session = Assert.Single(await db.UserSessions.Where(candidate => candidate.UserId == user.Id).ToListAsync());
        Assert.NotNull(session.RevokedAtUtc);
        Assert.Contains(await db.AuthenticationAuditEntries.Where(entry => entry.UserId == user.Id).ToListAsync(),
            entry => entry.EventType == "logout" && entry.CompanyId == user.CompanyId && entry.Succeeded);
    }

    [Fact]
    public async Task AccountRecoveryApi_VerifiesEmailUsesUniformResetResponseAndConsumesTokensOnce()
    {
        using var isolatedFactory = new BrassLedgerApiFactory(configureSecurityEmail: true);
        using var authenticatedClient = await CreateAuthenticatedClientAsync(isolatedFactory);

        var verificationRequest = await authenticatedClient.PostAsync("/api/auth/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, verificationRequest.StatusCode);
        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var verificationMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages);
        var verificationToken = ExtractAccountActionToken(verificationMessage.Body);

        using var anonymousClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verification = await anonymousClient.PostAsJsonAsync("/api/auth/email-verification/complete", new { Token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymousClient.PostAsJsonAsync(
            "/api/auth/email-verification/complete", new { Token = verificationToken })).StatusCode);

        var emailChange = await authenticatedClient.PostAsJsonAsync("/api/auth/email/change", new
        {
            NewEmail = "controller-replacement@example.test",
            CurrentPassword = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, emailChange.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await authenticatedClient.GetAsync("/api/dashboard")).StatusCode);
        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var replacementMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("new BrassLedger email", StringComparison.Ordinal));
        Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("email address was changed", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.PostAsJsonAsync("/api/auth/email-verification/complete", new
        {
            Token = ExtractAccountActionToken(replacementMessage.Body)
        })).StatusCode);

        var unknownReset = await anonymousClient.PostAsJsonAsync("/api/auth/password-reset/request", new { Identifier = "missing@example.test" });
        var knownReset = await anonymousClient.PostAsJsonAsync("/api/auth/password-reset/request", new { Identifier = "controller" });
        Assert.Equal(HttpStatusCode.Accepted, unknownReset.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownReset.StatusCode);
        Assert.Equal(await unknownReset.Content.ReadAsStringAsync(), await knownReset.Content.ReadAsStringAsync());

        await DispatchAllSecurityEmailAsync(isolatedFactory);
        var resetMessage = Assert.Single(isolatedFactory.SecurityEmailTransport.Messages, message => message.Subject.Contains("Reset", StringComparison.Ordinal));
        var resetToken = ExtractAccountActionToken(resetMessage.Body);
        var reset = await anonymousClient.PostAsJsonAsync("/api/auth/account-action", new
        {
            Token = resetToken,
            NewPassword = "API recovery password 2026",
            ConfirmPassword = "API recovery password 2026"
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymousClient.PostAsJsonAsync("/api/auth/account-action", new
        {
            Token = resetToken,
            NewPassword = "Another API recovery password 2026",
            ConfirmPassword = "Another API recovery password 2026"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = "API recovery password 2026"
        })).StatusCode);
    }

    [Fact]
    public async Task ActiveCompanySwitch_ToPrivilegedMembershipRequiresMfaEnrollment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var companyId = Guid.NewGuid();
        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.UserName == "controller");
            dbContext.Companies.Add(new Company
            {
                Id = companyId,
                Name = "Secondary company",
                LegalName = "Secondary Company LLC",
                TaxId = "12-3456789",
                BaseCurrency = "CAD",
                FiscalYearStartMonth = 1
            });
            await SecurityAdministrationService.EnsureBuiltInRolesAsync(dbContext, companyId);
            dbContext.CompanyMemberships.Add(new CompanyMembership
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CompanyId = companyId,
                Role = "Administrator",
                IsOwner = true,
                IsActive = true,
                GrantedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var switchResponse = await client.PostAsJsonAsync("/api/auth/active-company", new { CompanyId = companyId });

        Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.Equal(companyId.ToString(), me.GetProperty("companyId").GetString());
        Assert.True(me.GetProperty("mfaEnrollmentRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_ThrottlesExcessiveRequestsFromOneNetworkAddress()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var attempt = 0; attempt < BrassLedgerAuthenticationDefaults.LoginRequestsPerMinute; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                UserName = $"missing-user-{attempt}",
                Password = "invalid-password"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "one-request-too-many",
            Password = "invalid-password"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), throttled.Headers.RetryAfter?.Delta);
        var problem = await throttled.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("too_many_login_attempts", problem.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ApiLogin_RequiresAndCompletesMfa_AndConsumesRecoveryCodeOnce()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        var recoveryCodes = await EnrollMfaAsync(isolatedFactory, "controller");
        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var passwordResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, passwordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard")).StatusCode);
        var challenge = await passwordResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(challenge.GetProperty("mfaRequired").GetBoolean());
        var challengeToken = challenge.GetProperty("challengeToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(challengeToken));

        var mfaResponse = await client.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challengeToken,
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.OK, mfaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard")).StatusCode);
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("mfaAuthenticated").GetBoolean());

        using var replayClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var replayResponse = await replayClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challengeToken,
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        var nextPasswordResponse = await replayClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        var nextChallenge = await nextPasswordResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var reusedRecoveryResponse = await replayClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = nextChallenge.GetProperty("challengeToken").GetString(),
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reusedRecoveryResponse.StatusCode);
    }

    [Fact]
    public async Task PrivilegedRoleWithoutMfa_IsRestrictedToAccountSecurityUntilEnrollment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        await using (var scope = isolatedFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrassLedgerDbContext>();
            var controllerRole = await db.AccessRoles.SingleAsync(role => role.Name == "Controller");
            controllerRole.RequiresMfa = true;
            await db.SaveChangesAsync();
        }

        using var client = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var restrictedLogin = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.OK, restrictedLogin.StatusCode);
        var restrictedIdentity = await restrictedLogin.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(restrictedIdentity.GetProperty("mfaEnrollmentRequired").GetBoolean());
        Assert.False(restrictedIdentity.GetProperty("mfaAuthenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard")).StatusCode);
        var securityIdentity = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.True(securityIdentity.GetProperty("mfaEnrollmentRequired").GetBoolean());

        var recoveryCodes = await EnrollMfaAsync(isolatedFactory, "controller");
        using var verifiedClient = isolatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var passwordStage = await verifiedClient.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = "controller",
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });
        Assert.Equal(HttpStatusCode.Accepted, passwordStage.StatusCode);
        var challenge = await passwordStage.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var verification = await verifiedClient.PostAsJsonAsync("/api/auth/mfa", new
        {
            ChallengeToken = challenge.GetProperty("challengeToken").GetString(),
            VerificationCode = recoveryCodes[0]
        });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await verifiedClient.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task TrialBalanceReport_ReturnsCsvForReportingUser()
    {
        using var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/trial-balance.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("Account,Type,Balance", csv);
        Assert.Contains("1000", csv);
    }

    [Fact]
    public async Task CreateInvoice_PostsAndUpdatesReceivablesWorkspace()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();

        var response = await client.PostAsJsonAsync("/api/invoices", new CreateInvoiceRequest(
            customer.Id,
            "INV-API-TEST-1",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            125m,
            0m,
            "4000",
            "API workflow test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Receivables.OpenBalance + 125m, after!.Receivables.OpenBalance);
        Assert.Contains(after.Receivables.Invoices, invoice => invoice.InvoiceNumber == "INV-API-TEST-1" && invoice.BalanceDue == 125m);

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BrassLedgerDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var posting = await dbContext.JournalEntries.SingleAsync(entry => entry.Reference == "INV-API-TEST-1");
        Assert.NotNull(posting.PostedByUserId);
        Assert.True(posting.PostedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task CustomerPaymentApi_AppliesMultipleInvoices_PreservesDeposit_AndReturnsPayment()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var customer = before!.Receivables.Customers.First();
        var bank = before.Treasury.BankAccounts.First();

        async Task<Guid> CreateInvoiceAsync(string number, decimal amount)
        {
            var response = await client.PostAsJsonAsync("/api/invoices", new CreateInvoiceRequest(
                customer.Id, number, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), amount, 0m, "4000", "Payment API workflow"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<TransactionResult>();
            Assert.NotNull(result?.Id);
            return result!.Id!.Value;
        }

        var firstInvoiceId = await CreateInvoiceAsync("INV-API-PAY-1", 40m);
        var secondInvoiceId = await CreateInvoiceAsync("INV-API-PAY-2", 35m);
        var paymentResponse = await client.PostAsJsonAsync("/api/customer-payments", new RecordCustomerPaymentRequest(
            customer.Id, bank.Id, new DateOnly(2026, 5, 2), 90m, "DEP-API-PAY-1", "ACH",
            [new PaymentDocumentApplicationRequest(firstInvoiceId, 40m), new PaymentDocumentApplicationRequest(secondInvoiceId, 35m)]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var paymentResult = await paymentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(paymentResult?.Id);

        var paid = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(paid);
        var recorded = Assert.Single(paid!.Receivables.Payments!, payment => payment.Id == paymentResult!.Id);
        Assert.Equal(75m, recorded.AppliedAmount);
        Assert.Equal(15m, recorded.UnappliedAmount);
        Assert.Equal(2, recorded.Applications.Count);

        var returnResponse = await client.PostAsJsonAsync("/api/subledger-payments/reverse", new ReverseSubledgerPaymentRequest(
            paymentResult!.Id!.Value, new DateOnly(2026, 5, 3), "Bank returned the ACH", "Returned"));
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returned = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(returned);
        Assert.Equal("Returned", returned!.Receivables.Payments!.Single(payment => payment.Id == paymentResult.Id).Status);
        Assert.Equal(40m, returned.Receivables.Invoices.Single(invoice => invoice.Id == firstInvoiceId).BalanceDue);
        Assert.Equal(35m, returned.Receivables.Invoices.Single(invoice => invoice.Id == secondInvoiceId).BalanceDue);
    }

    [Fact]
    public async Task BankingApi_ImportsStatementsAndReversesTransfersAndAdjustments()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var fromBank = before!.Treasury.BankAccounts.First();
        var toBank = before.Treasury.BankAccounts.Last();

        var importResponse = await client.PostAsJsonAsync("/api/bank-statements/import", new ImportBankStatementRequest(
            fromBank.Id, "api-statement.csv", "CSV", "ExternalId,Date,Amount,Payee\nAPI-BANK-1,2026-05-01,15.00,Customer"));
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<BankStatementImportResult>();
        Assert.Equal(1, imported?.ImportedCount);

        var transferResponse = await client.PostAsJsonAsync("/api/bank-transfers", new CreateBankTransferRequest(
            fromBank.Id, toBank.Id, new DateOnly(2026, 5, 2), 25m, "TR-API-BANK-1", "API transfer"));
        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transfer = await transferResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(transfer?.Id);
        var reverseTransferResponse = await client.PostAsJsonAsync("/api/bank-transfers/reverse", new ReverseBankTransferRequest(
            transfer!.Id!.Value, new DateOnly(2026, 5, 3), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseTransferResponse.StatusCode);

        var offsetAccount = before.GeneralLedger.Accounts.First(account => account.Type == "Expense" && !account.IsControlAccount).Number;
        var adjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments", new CreateReconciliationAdjustmentRequest(
            fromBank.Id, new DateOnly(2026, 5, 4), 5m, offsetAccount, "ADJ-API-BANK-1", "API bank interest"));
        Assert.Equal(HttpStatusCode.Created, adjustmentResponse.StatusCode);
        var adjustment = await adjustmentResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(adjustment?.Id);
        var reverseAdjustmentResponse = await client.PostAsJsonAsync("/api/bank-reconciliation-adjustments/reverse", new ReverseReconciliationAdjustmentRequest(
            adjustment!.Id!.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.OK, reverseAdjustmentResponse.StatusCode);

        var after = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(after);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance, after!.Treasury.BankAccounts.Single(bank => bank.Id == fromBank.Id).CurrentBalance);
        Assert.Equal(before.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance, after.Treasury.BankAccounts.Single(bank => bank.Id == toBank.Id).CurrentBalance);
        Assert.Equal("Reversed", after.Treasury.Transfers!.Single(item => item.Id == transfer.Id).Status);
        Assert.Equal("Reversed", after.Treasury.Adjustments!.Single(item => item.Id == adjustment.Id).Status);
    }

    [Fact]
    public async Task JournalDraftApi_RequiresApprovalBeforePostingAndPreservesReversalLinks()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);

        var draftResponse = await client.PostAsJsonAsync("/api/journal-entry-drafts", new SaveJournalEntryDraftRequest(
            null,
            new DateOnly(2026, 5, 4),
            "JE-API-LIFECYCLE-1",
            "API journal lifecycle",
            [new JournalLineRequest("1000", 40m, 0m, "Cash"), new JournalLineRequest("4000", 0m, 40m, "Revenue")]));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(draft?.Id);

        var prematurePost = await client.PostAsync($"/api/journal-entry-drafts/{draft!.Id}/post", null);
        Assert.Equal(HttpStatusCode.BadRequest, prematurePost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/journal-entry-drafts/{draft.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/journal-entry-drafts/{draft.Id}/post", null)).StatusCode);

        var afterPosting = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterPosting);
        Assert.Equal(before!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance + 40m, afterPosting!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);

        var reversalResponse = await client.PostAsJsonAsync("/api/journal-entries/reverse", new ReverseJournalEntryRequest(draft.Id.Value, new DateOnly(2026, 5, 5), "API correction"));
        Assert.Equal(HttpStatusCode.Created, reversalResponse.StatusCode);
        var afterReversal = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(afterReversal);
        Assert.Equal(before.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance, afterReversal!.GeneralLedger.Accounts.Single(account => account.Number == "1000").Balance);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.Id == draft.Id && entry.Status == "Reversed" && entry.ReversedByJournalEntryId.HasValue);
        Assert.Contains(afterReversal.GeneralLedger.RecentEntries, entry => entry.ReversalOfJournalEntryId == draft.Id);
    }

    [Fact]
    public async Task PayrollApi_PreservesDraftApprovalPostingAndReversalWorkflow()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory, "payroll");
        var before = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(before);
        var employee = before!.Payroll.Employees.First();
        var bank = before.Treasury.BankAccounts.Single(account => account.LedgerAccountNumber == "1010");
        var timecardRequest = new SavePayrollTimecardDraftRequest(null, employee.Id, new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 6),
            [new PayrollTimeEntryInput(new DateOnly(2026, 6, 1), "REG", "Regular", 8m, 25m, 200m, WorkState: employee.State)], "API timecard");
        var timecardResponse = await client.PostAsJsonAsync("/api/payroll-timecards/drafts", timecardRequest);
        Assert.Equal(HttpStatusCode.Created, timecardResponse.StatusCode);
        var timecardResult = await timecardResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecardResult!.Id);
        Assert.Equal("Draft", timecard.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-timecards/submit", new SubmitPayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).StatusCode);
        timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-timecards/approve", new ApprovePayrollTimecardRequest(timecard.Id, timecard.ConcurrencyToken))).StatusCode);
        timecardWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        timecard = timecardWorkspace!.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal("Approved", timecard.Status);

        var request = new PostEmployeePayrollRunRequest(bank.Id, new DateOnly(2026, 6, 12), "PR-API-LIFECYCLE-1", [new EmployeePayrollInput(employee.Id, 500m)], new DateOnly(2026, 5, 31), new DateOnly(2026, 6, 6), ApprovedTimecardIds: [timecard.Id]);

        var preview = await client.PostAsJsonAsync("/api/payroll-runs/employee-preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewResult = await preview.Content.ReadFromJsonAsync<PayrollRunEstimate>();
        Assert.Equal(200m, previewResult!.GrossPayroll);
        var draftResponse = await client.PostAsJsonAsync("/api/payroll-runs/drafts", request);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draftResult = await draftResponse.Content.ReadFromJsonAsync<TransactionResult>();
        Assert.NotNull(draftResult?.Id);

        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == draftResult!.Id);
        Assert.Equal("Draft", run.Status);
        Assert.Equal(200m, run.GrossPayroll);
        timecard = workspace.Payroll.Timecards!.Single(candidate => candidate.Id == timecard.Id);
        Assert.Equal("Consumed", timecard.Status);
        Assert.Equal(run.Id, timecard.PayrollRunId);
        var reused = request with { Reference = "PR-API-LIFECYCLE-REUSE" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/drafts", reused)).StatusCode);
        Assert.Equal(bank.CurrentBalance, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/post", new PostApprovedPayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-runs/approve", new ApprovePayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Approved", run.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-runs/post", new PostApprovedPayrollRunRequest(run.Id, run.ConcurrencyToken))).StatusCode);

        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Posted", run.Status);
        Assert.NotNull(run.JournalEntryId);
        Assert.Equal(bank.CurrentBalance - run.NetPay, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        var register = await client.GetFromJsonAsync<PayrollRegister>($"/api/payroll-runs/{run.Id}/register");
        Assert.NotNull(register);
        Assert.Equal(run.NetPay, register!.Employees.Sum(item => item.NetPay));
        var statement = await client.GetFromJsonAsync<PayrollPayStatement>($"/api/payroll-runs/{run.Id}/employees/{employee.Id}/pay-statement");
        Assert.NotNull(statement);
        Assert.Equal(run.NetPay, statement!.NetPay);
        Assert.Equal(statement.GrossPay, statement.Earnings.Sum(item => item.Amount));
        var registerCsv = await client.GetAsync($"/api/payroll-runs/{run.Id}/register.csv");
        Assert.Equal("text/csv", registerCsv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"TOTAL\"", await registerCsv.Content.ReadAsStringAsync());
        var depositScheduleResponse = await client.PutAsJsonAsync("/api/payroll-deposit-schedules", new SavePayrollDepositScheduleRequest(null, 2026, "Monthly", 40000m, new DateOnly(2024, 7, 1), new DateOnly(2025, 6, 30), 50000m, 100000m, 2500m, "[]", "[\"2026-01-01\",\"2026-01-19\",\"2026-02-16\",\"2026-04-16\",\"2026-05-25\",\"2026-06-19\",\"2026-07-03\",\"2026-09-07\",\"2026-10-12\",\"2026-11-11\",\"2026-11-26\",\"2026-12-25\"]", "https://www.irs.gov/publications/p15", "https://www.irs.gov/publications/p509", new DateOnly(2026, 8, 25), "API approval test", true, true));
        Assert.Equal(HttpStatusCode.OK, depositScheduleResponse.StatusCode);
        var depositWorkspace = await client.GetFromJsonAsync<PayrollDepositScheduleWorkspace>("/api/payroll-deposit-schedules");
        Assert.Contains(depositWorkspace!.Configurations, item => item.TaxYear == 2026 && item.IsApproved);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-disaster-relief")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ssa-wage-files")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ssa-original-wage-files")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-deduction-configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-payment-files")).StatusCode);
        var paymentFileResponse = await client.PostAsJsonAsync("/api/payroll-payment-files", new GeneratePayrollPaymentFileRequest(run.Id, "CheckRegisterCsv"));
        Assert.Equal(HttpStatusCode.Created, paymentFileResponse.StatusCode);
        var paymentFileResult = await paymentFileResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var paymentFileDownload = await client.GetAsync($"/api/payroll-payment-files/{paymentFileResult!.Id}/download");
        Assert.Equal("text/csv", paymentFileDownload.Content.Headers.ContentType?.MediaType);
        Assert.Contains("CheckReference", await paymentFileDownload.Content.ReadAsStringAsync());
        using (var nonPayrollClient = await CreateAuthenticatedClientAsync(isolatedFactory, "controller"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-runs/{run.Id}/register")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-runs/{run.Id}/employees/{employee.Id}/pay-statement")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-filings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-filing-corrections")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.PostAsJsonAsync("/api/payroll-filing-corrections/w2c/drafts", new SaveW2CorrectionDraftRequest(null, Guid.NewGuid(), new DateOnly(2026, 8, 25), "Unauthorized correction attempt must never reach the protected service.", true, "TEST-EVIDENCE"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-deposit-schedules")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-disaster-relief")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/ssa-wage-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/ssa-original-wage-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-deduction-configuration")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync("/api/payroll-payment-files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nonPayrollClient.GetAsync($"/api/payroll-payment-files/{paymentFileResult.Id}/download")).StatusCode);
        }
        var filingResponse = await client.PostAsJsonAsync("/api/payroll-filings/drafts", new SavePayrollFilingDraftRequest(null, "941", 2026, 2));
        Assert.Equal(HttpStatusCode.Created, filingResponse.StatusCode);
        var filingResult = await filingResponse.Content.ReadFromJsonAsync<TransactionResult>();
        var filing = await client.GetFromJsonAsync<PayrollFilingSnapshot>($"/api/payroll-filings/{filingResult!.Id}");
        Assert.NotNull(filing);
        Assert.True(filing!.Data.GetProperty("WagesTipsAndOtherCompensation").GetDecimal() > 0);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-filings/approve", new ApprovePayrollFilingRequest(filing.Id, filing.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/payroll-filing-corrections")).StatusCode);
        filing = await client.GetFromJsonAsync<PayrollFilingSnapshot>($"/api/payroll-filings/{filing.Id}");
        Assert.Equal("Approved", filing!.Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-filings/reopen", new ReopenPayrollFilingRequest(filing.Id, "API correction test", filing.ConcurrencyToken))).StatusCode);
        var liability = workspace.Payroll.Liabilities!.First(item => item.Status == "Open");
        var liabilityPaymentResponse = await client.PostAsJsonAsync("/api/payroll-liability-payments", new RecordPayrollLiabilityPaymentRequest(bank.Id, new DateOnly(2026, 6, 13), "API-TAX-PAY-1", "Tax agency", "EFT", [new PayrollLiabilityPaymentApplicationInput(liability.Id, liability.OutstandingAmount)]));
        Assert.Equal(HttpStatusCode.Created, liabilityPaymentResponse.StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var liabilityPayment = workspace!.Payroll.LiabilityPayments!.Single(item => item.Reference == "API-TAX-PAY-1");
        Assert.Equal("Paid", workspace.Payroll.Liabilities!.Single(item => item.Id == liability.Id).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-liability-payments/reverse", new ReversePayrollLiabilityPaymentRequest(liabilityPayment.Id, new DateOnly(2026, 6, 13), "API correction", liabilityPayment.ConcurrencyToken))).StatusCode);
        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/payroll-runs/reverse", new ReversePayrollRunRequest(run.Id, new DateOnly(2026, 6, 13), "API payroll correction", run.ConcurrencyToken))).StatusCode);

        workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        run = workspace!.Payroll.Runs!.Single(candidate => candidate.Id == run.Id);
        Assert.Equal("Reversed", run.Status);
        Assert.NotNull(run.ReversalJournalEntryId);
        Assert.Equal(bank.CurrentBalance, workspace.Treasury.BankAccounts.Single(account => account.Id == bank.Id).CurrentBalance);
        var paymentFileWorkspace = await client.GetFromJsonAsync<PayrollPaymentFileWorkspace>("/api/payroll-payment-files");
        Assert.Equal("Voided", paymentFileWorkspace!.Files.Single(item => item.Id == paymentFileResult.Id).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/payroll-runs/cancel", new CancelPayrollRunRequest(run.Id, "Too late", run.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task QuickBooksOnlineInterchange_ExportsAndImportsCoreLists()
    {
        using var isolatedFactory = new BrassLedgerApiFactory();
        using var client = await CreateAuthenticatedClientAsync(isolatedFactory);

        var export = await client.GetAsync("/api/interchange/quickbooks-online/chart-of-accounts.csv");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportedCsv = await export.Content.ReadAsStringAsync();
        Assert.Contains("\"Account Name\",\"Type\",\"Detail Type\",\"Account Number\"", exportedCsv);
        Assert.Contains("\"Accounts Receivable\",\"Accounts Receivable\",\"Accounts Receivable\",\"1100\"", exportedCsv);
        Assert.Contains("\"Sales Tax Payable\",\"Other Current Liability\",\"Sales tax payable\",\"2100\"", exportedCsv);
        var invoiceExport = await client.GetStringAsync("/api/interchange/quickbooks-online/invoices.csv");
        Assert.Contains("\"Invoice No.\",\"Customer\",\"Invoice Date\",\"Due Date\",\"Item Amount\",\"Item Description\",\"Quantity\",\"Rate\"", invoiceExport);
        Assert.Contains("INV-24021", invoiceExport);
        Assert.DoesNotContain("INV-24015", invoiceExport);

        var token = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/antiforgery/token");
        Assert.NotNull(token);
        using var form = new MultipartFormDataContent();
        form.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        form.Add(new StringContent("Display Name,Company Name,Email,Customer Number\r\n\"QuickBooks\nImport Co\",QuickBooks Import Co,import@example.test,QBO-IMPORT-1"), "file", "quickbooks-customers.csv");
        var preview = await client.PostAsync("/api/interchange/quickbooks-online/customers?dryRun=true", form);
        Assert.True(preview.StatusCode == HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewResult = await preview.Content.ReadFromJsonAsync<AccountingInterchangeImportResult>();
        Assert.True(previewResult!.DryRun); Assert.Equal(1, previewResult.ImportedCount); Assert.Equal(64, previewResult.ContentSha256.Length);
        var previewWorkspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.DoesNotContain(previewWorkspace!.Receivables.Customers, customer => customer.CustomerNumber == "QBO-IMPORT-1");
        using var importForm = new MultipartFormDataContent();
        importForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        importForm.Add(new StringContent("Display Name,Company Name,Email,Customer Number\r\n\"QuickBooks\nImport Co\",QuickBooks Import Co,import@example.test,QBO-IMPORT-1"), "file", "quickbooks-customers.csv");
        var import = await client.PostAsync("/api/interchange/quickbooks-online/customers", importForm);
        Assert.True(import.StatusCode == HttpStatusCode.OK, await import.Content.ReadAsStringAsync());
        var workspace = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.NotNull(workspace);
        Assert.Contains(workspace!.Receivables.Customers, customer => customer.CustomerNumber == "QBO-IMPORT-1" && customer.Name == "QuickBooks\nImport Co");

        var controlsResponse = await client.GetAsync("/api/accounting-controls?auditEntryLimit=20");
        Assert.True(controlsResponse.StatusCode == HttpStatusCode.OK, await controlsResponse.Content.ReadAsStringAsync());
        var controls = await controlsResponse.Content.ReadFromJsonAsync<AccountingControlsSnapshot>();
        var validationAudit = Assert.Single(controls!.AuditEntries, entry => entry.Action == "accounting-interchange.quickbooks.validated");
        var importAudit = Assert.Single(controls.AuditEntries, entry => entry.Action == "accounting-interchange.quickbooks.imported");
        Assert.Contains(previewResult.ContentSha256, validationAudit.DetailJson);
        Assert.Contains(previewResult.ContentSha256, importAudit.DetailJson);
        Assert.Contains("quickbooks-customers.csv", importAudit.DetailJson);

        using var journalForm = new MultipartFormDataContent();
        journalForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        journalForm.Add(new StringContent("Journal No.,Journal Date,Reference,Journal/Description,Account Name,Debits,Credits,Line Description\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Operating Cash,25.00,0.00,Cash\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Product Revenue,0.00,25.00,Revenue"), "file", "quickbooks-journals.csv");
        var journalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", journalForm);
        Assert.True(journalImport.StatusCode == HttpStatusCode.OK, await journalImport.Content.ReadAsStringAsync());
        using var duplicateJournalForm = new MultipartFormDataContent();
        duplicateJournalForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        duplicateJournalForm.Add(new StringContent("Journal No.,Journal Date,Reference,Journal/Description,Account Name,Debits,Credits,Line Description\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Operating Cash,25.00,0.00,Cash\r\nQBO-JE-1,2026-05-01,QBO-JE-1,Imported general journal,Product Revenue,0.00,25.00,Revenue"), "file", "quickbooks-journals-retry.csv");
        var duplicateJournalImport = await client.PostAsync("/api/interchange/quickbooks-online/journal-entries", duplicateJournalForm);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateJournalImport.StatusCode);
        using var malformedForm = new MultipartFormDataContent();
        malformedForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        malformedForm.Add(new StringContent("Display Name,Customer Number\r\n\"unterminated,QBO-BAD-1"), "file", "malformed-customers.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/interchange/quickbooks-online/customers?dryRun=true", malformedForm)).StatusCode);
        var journalExport = await client.GetStringAsync("/api/interchange/quickbooks-online/journal-entries.csv");
        Assert.Contains("\"Journal No.\",\"Journal Date\",\"Reference\",\"Journal/Description\",\"Account Name\",\"Debits\",\"Credits\",\"Line Description\"", journalExport);
        Assert.Contains("QBO-JE-1", journalExport);
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrassLedgerDbContext>();
            var otherCompany = new Company { Id = Guid.NewGuid(), Name = $"Other Company {Guid.NewGuid():N}", LegalName = "Other Company", BaseCurrency = "USD", FiscalYearStartMonth = 1 };
            db.Companies.Add(otherCompany);
            db.AccountingInterchangeBatches.Add(new AccountingInterchangeBatch { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, ProviderCode = "quickbooks-online", EntityType = "customers", FileName = "other-company.csv", ContentSha256 = new string('a', 64), Status = "Imported", RowCount = 1, ImportedCount = 1, ProcessedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var batches = await client.GetFromJsonAsync<AccountingInterchangeBatchSnapshot[]>("/api/interchange/batches");
        Assert.Equal(5, batches!.Length);
        Assert.DoesNotContain(batches, batch => batch.FileName == "other-company.csv");
        Assert.Contains(batches, batch => batch.Status == "Validated" && batch.IsDryRun && batch.EntityType == "customers");
        Assert.Contains(batches, batch => batch.Status == "Imported" && !batch.IsDryRun && batch.ImportedCount == 1);
        Assert.Contains(batches, batch => batch.Status == "DuplicateRejected" && batch.DuplicateCount == 2 && batch.RejectedCount == 2 && batch.Rejections.Count == 1);
        Assert.Contains(batches, batch => batch.Status == "Rejected" && batch.FileName == "malformed-customers.csv" && batch.RejectedCount == 1 && batch.ContentSha256.Length == 64);

        var beforeInvoiceImport = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var receivablesBefore = beforeInvoiceImport!.Receivables.OpenBalance;
        const string invoiceCsv = "Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Item Description,Quantity,Rate,Income Account\r\nQBO-INV-1,C-1003,2026-05-10,2026-06-09,50.00,Imported service,2,25.00,Product Revenue\r\nQBO-INV-1,C-1003,2026-05-10,2026-06-09,25.00,Imported materials,1,25.00,4000";
        using var invoicePreviewForm = new MultipartFormDataContent();
        invoicePreviewForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        invoicePreviewForm.Add(new StringContent(invoiceCsv), "file", "quickbooks-invoices.csv");
        var invoicePreview = await client.PostAsync("/api/interchange/quickbooks-online/invoices?dryRun=true", invoicePreviewForm);
        Assert.True(invoicePreview.StatusCode == HttpStatusCode.OK, await invoicePreview.Content.ReadAsStringAsync());
        var afterInvoicePreview = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.DoesNotContain(afterInvoicePreview!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-INV-1");
        Assert.Equal(receivablesBefore, afterInvoicePreview.Receivables.OpenBalance);

        using var invoiceImportForm = new MultipartFormDataContent();
        invoiceImportForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        invoiceImportForm.Add(new StringContent(invoiceCsv), "file", "quickbooks-invoices.csv");
        var invoiceImport = await client.PostAsync("/api/interchange/quickbooks-online/invoices", invoiceImportForm);
        Assert.True(invoiceImport.StatusCode == HttpStatusCode.OK, await invoiceImport.Content.ReadAsStringAsync());
        var afterInvoiceImport = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        var importedDraft = Assert.Single(afterInvoiceImport!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-INV-1");
        Assert.Equal("Draft", importedDraft.Status);
        Assert.Equal(receivablesBefore, afterInvoiceImport.Receivables.OpenBalance);

        using var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/subledger-document-workflows/{importedDraft.Id}/approve");
        approveRequest.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(approveRequest)).StatusCode);
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/subledger-document-workflows/{importedDraft.Id}/post");
        postRequest.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(postRequest)).StatusCode);
        var afterInvoicePost = await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace");
        Assert.Equal(receivablesBefore + 75m, afterInvoicePost!.Receivables.OpenBalance);
        Assert.Contains(afterInvoicePost.Receivables.Invoices, invoice => invoice.InvoiceNumber == "QBO-INV-1" && invoice.TotalAmount == 75m);
        using var taxableInvoiceForm = new MultipartFormDataContent();
        taxableInvoiceForm.Headers.Add("X-CSRF-TOKEN", token!["requestToken"]);
        taxableInvoiceForm.Add(new StringContent("Invoice No.,Customer,Invoice Date,Due Date,Item Amount,Tax Amount\r\nQBO-TAX-1,C-1003,2026-05-10,2026-06-09,50.00,3.00"), "file", "taxable-quickbooks-invoices.csv");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/interchange/quickbooks-online/invoices?dryRun=true", taxableInvoiceForm)).StatusCode);
        var invoiceBatches = await client.GetFromJsonAsync<AccountingInterchangeBatchSnapshot[]>("/api/interchange/batches");
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "Validated" && batch.ImportedCount == 1);
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "DraftsCreated" && batch.ImportedCount == 1);
        Assert.Contains(invoiceBatches!, batch => batch.EntityType == "invoices" && batch.Status == "Rejected" && batch.FileName == "taxable-quickbooks-invoices.csv");
        Assert.DoesNotContain((await client.GetFromJsonAsync<BusinessWorkspaceSnapshot>("/api/workspace"))!.Receivables.Workflows ?? [], workflow => workflow.DocumentNumber == "QBO-TAX-1");

        using var unauthorizedClient = await CreateAuthenticatedClientAsync(isolatedFactory, "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorizedClient.GetAsync("/api/interchange/quickbooks-online/chart-of-accounts.csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorizedClient.GetAsync("/api/interchange/batches")).StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program>? factory = null, string userName = "controller")
    {
        var testFactory = factory ?? _factory;
        var client = testFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            UserName = userName,
            Password = BrassLedgerAuthenticationDefaults.SeededPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/antiforgery/token");
        return token.GetProperty("requestToken").GetString()!;
    }

    private static async Task<IReadOnlyList<string>> EnrollMfaAsync(BrassLedgerApiFactory factory, string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var signedIn = await authentication.AuthenticateAsync(
            userName,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(AuthenticationOutcome.Succeeded, signedIn.Outcome);
        var enrollment = await authentication.BeginMfaEnrollmentAsync(
            signedIn.User!.UserId,
            signedIn.User.CompanyId,
            BrassLedgerAuthenticationDefaults.SeededPassword,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(MfaOperationOutcome.Succeeded, enrollment.Outcome);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TotpService.TimeStepSeconds;
        var code = TotpService.ComputeCode(TotpService.DecodeBase32(enrollment.Secret), step);
        var enabled = await authentication.EnableMfaAsync(
            signedIn.User.UserId,
            signedIn.User.CompanyId,
            code,
            "127.0.0.1",
            "api-mfa-setup");
        Assert.Equal(MfaOperationOutcome.Succeeded, enabled.Outcome);
        return enrollment.RecoveryCodes!;
    }

    private static async Task DispatchAllSecurityEmailAsync(BrassLedgerApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISecurityEmailOutboxDispatcher>();
        while (await dispatcher.DispatchNextAsync()) { }
    }

    private static string ExtractAccountActionToken(string body)
    {
        var match = Regex.Match(body, @"https://\S+", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return QueryHelpers.ParseQuery(new Uri(match.Value.Trim()).Query)["token"].ToString();
    }
}

public sealed class BrassLedgerApiFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "BrassLedger.Api.Tests", Guid.NewGuid().ToString("N"));
    private readonly bool _configureSecurityEmail;

    public BrassLedgerApiFactory() : this(false)
    {
    }

    internal BrassLedgerApiFactory(bool configureSecurityEmail)
    {
        _configureSecurityEmail = configureSecurityEmail;
    }

    public RecordingSecurityEmailTransport SecurityEmailTransport { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_contentRootPath);

        builder.UseEnvironment("Development");
        builder.UseSetting(WebHostDefaults.ContentRootKey, _contentRootPath);
        if (_configureSecurityEmail)
        {
            builder.UseSetting("AccountEmail:Enabled", "true");
            builder.UseSetting("AccountEmail:PublicBaseUrl", "https://ledger.example.test");
            builder.UseSetting("AccountEmail:Host", "smtp.example.test");
            builder.UseSetting("AccountEmail:FromAddress", "security@example.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISecurityEmailTransport>();
                services.AddSingleton<ISecurityEmailTransport>(SecurityEmailTransport);
            });
        }
    }

    public new void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(_contentRootPath))
        {
            try
            {
                Directory.Delete(_contentRootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public sealed class RecordingSecurityEmailTransport : ISecurityEmailTransport
    {
        public bool IsConfigured => true;
        public List<RecordedSecurityEmail> Messages { get; } = [];

        public Task<string> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            Messages.Add(new RecordedSecurityEmail(recipient, subject, body));
            return Task.FromResult($"<{Guid.NewGuid():N}@example.test>");
        }
    }

    public sealed record RecordedSecurityEmail(string Recipient, string Subject, string Body);
}
