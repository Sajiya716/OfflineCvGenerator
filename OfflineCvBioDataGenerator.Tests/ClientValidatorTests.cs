using System;
using System.Linq;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.Services;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class ClientValidatorTests
    {
        [Fact]
        public void ValidateForSave_CompleteClient_ReturnsNoErrors()
        {
            var client = TestData.NewClient(dob: new DateTime(1990, 1, 1));

            Assert.Empty(ClientValidator.ValidateForSave(client));
        }

        [Fact]
        public void ValidateForSave_NullClient_ReturnsAnError()
        {
            Assert.NotEmpty(ClientValidator.ValidateForSave(null));
        }

        [Fact]
        public void ValidateForSave_MissingFullName_IsAnError()
        {
            var client = TestData.NewClient(fullName: "   ");

            var errors = ClientValidator.ValidateForSave(client);

            Assert.Contains(errors, e => e.Contains("Full Name"));
        }

        [Fact]
        public void ValidateForSave_MissingNic_IsAnError()
        {
            var client = TestData.NewClient(nic: "");

            var errors = ClientValidator.ValidateForSave(client);

            Assert.Contains(errors, e => e.Contains("NIC"));
        }

        [Fact]
        public void ValidateForSave_TooLongName_IsAnError()
        {
            var client = TestData.NewClient(fullName: new string('A', ClientValidator.MaxNameLength + 1));

            Assert.Contains(ClientValidator.ValidateForSave(client), e => e.Contains("characters or fewer"));
        }

        [Fact]
        public void ValidateForSave_InvalidEmail_IsAnError()
        {
            var client = TestData.NewClient(email: "not-an-email");

            Assert.Contains(ClientValidator.ValidateForSave(client), e => e.Contains("Email"));
        }

        [Fact]
        public void ValidateForSave_EmptyEmail_IsAllowed()
        {
            var client = TestData.NewClient(email: "");

            Assert.Empty(ClientValidator.ValidateForSave(client));
        }

        [Theory]
        [InlineData("+94 77 123 4567")]
        [InlineData("0771234567")]
        [InlineData("(011) 234-5678")]
        public void ValidateForSave_ReasonablePhoneNumbers_AreAllowed(string phone)
        {
            var client = TestData.NewClient(phone: phone);

            Assert.Empty(ClientValidator.ValidateForSave(client));
        }

        [Fact]
        public void ValidateForSave_PhoneWithLetters_IsAnError()
        {
            var client = TestData.NewClient(phone: "call me on 0771234567");

            Assert.Contains(ClientValidator.ValidateForSave(client), e => e.Contains("Phone"));
        }

        [Fact]
        public void ValidateForSave_FutureDateOfBirth_IsAnError()
        {
            var client = TestData.NewClient(dob: DateTime.Today.AddDays(1));

            Assert.Contains(ClientValidator.ValidateForSave(client), e => e.Contains("future"));
        }

        [Fact]
        public void ValidateForSave_MissingOptionalFields_IsAllowed()
        {
            var client = new Client { FullName = "Only A Name", NIC = "123456789V" };

            Assert.Empty(ClientValidator.ValidateForSave(client));
        }

        [Fact]
        public void ValidateForPdf_MissingNameAndNic_ReportsBoth()
        {
            var client = new Client { FullName = "", NIC = "" };

            var errors = ClientValidator.ValidateForPdf(client);

            Assert.Contains(errors, e => e.Contains("Full Name"));
            Assert.Contains(errors, e => e.Contains("NIC"));
        }

        [Fact]
        public void ValidateForPdf_CompleteClient_Passes()
        {
            Assert.Empty(ClientValidator.ValidateForPdf(TestData.NewClient()));
        }

        [Theory]
        [InlineData("jane@example.com", true)]
        [InlineData("jane.doe+work@example.co.uk", true)]
        [InlineData("jane@", false)]
        [InlineData("@example.com", false)]
        [InlineData("jane example.com", false)]
        [InlineData("", false)]
        public void IsValidEmail_BehavesAsExpected(string email, bool expected)
        {
            Assert.Equal(expected, ClientValidator.IsValidEmail(email));
        }

        [Fact]
        public void Validation_ErrorMessagesAreAllNonEmpty()
        {
            var client = new Client { FullName = "", NIC = "", Email = "bad", Phone = "abc", DOB = DateTime.Today.AddYears(1) };

            var errors = ClientValidator.ValidateForSave(client);

            Assert.NotEmpty(errors);
            Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e)));
            Assert.Equal(errors.Count, errors.Distinct().Count());
        }
    }
}
