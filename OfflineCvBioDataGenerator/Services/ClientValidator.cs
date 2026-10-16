using System;
using System.Collections.Generic;
using System.Net.Mail;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.Services
{
    /// <summary>
    /// Input validation rules shared by the WinForms UI and the unit tests.
    /// Keeping the rules out of the form means they can be tested without a UI.
    /// </summary>
    public static class ClientValidator
    {
        /// <summary>Maximum sensible length for a name - also protects the PDF layout.</summary>
        public const int MaxNameLength = 120;

        /// <summary>Rules applied before a client is written to the database.</summary>
        public static IReadOnlyList<string> ValidateForSave(Client? client)
        {
            var errors = new List<string>();
            if (client == null)
            {
                errors.Add("No client data was provided.");
                return errors;
            }

            AddCommonErrors(client, errors);
            return errors;
        }

        /// <summary>
        /// Rules applied before a PDF is produced. Stricter than saving: a CV without a
        /// name or NIC is not acceptable output for the operator.
        /// </summary>
        public static IReadOnlyList<string> ValidateForPdf(Client? client)
        {
            var errors = new List<string>();
            if (client == null)
            {
                errors.Add("No client data was provided.");
                return errors;
            }

            AddCommonErrors(client, errors);

            if (string.IsNullOrWhiteSpace(client.FullName))
                errors.Insert(0, "Full Name is required.");

            if (string.IsNullOrWhiteSpace(client.NIC))
                errors.Add("NIC is required before a CV can be generated.");

            return errors;
        }

        private static void AddCommonErrors(Client client, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(client.FullName))
                errors.Add("Full Name is required.");

            if (client.FullName != null && client.FullName.Length > MaxNameLength)
                errors.Add($"Full Name must be {MaxNameLength} characters or fewer.");

            if (string.IsNullOrWhiteSpace(client.NIC))
                errors.Add("NIC is required.");

            if (!string.IsNullOrWhiteSpace(client.Email) && !IsValidEmail(client.Email))
                errors.Add("Email address does not look valid.");

            if (!string.IsNullOrWhiteSpace(client.Phone) && !LooksLikePhoneNumber(client.Phone))
                errors.Add("Phone number may only contain digits, spaces and + - ( ) .");

            if (client.DOB.HasValue && client.DOB.Value.Date > DateTime.Today)
                errors.Add("Date of Birth cannot be in the future.");
        }

        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;

            try
            {
                var address = new MailAddress(email.Trim());
                return string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikePhoneNumber(string phone)
        {
            foreach (char c in phone)
            {
                if (char.IsDigit(c)) continue;
                if (c is ' ' or '+' or '-' or '(' or ')' or '.') continue;
                return false;
            }
            return true;
        }
    }
}
