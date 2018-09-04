using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EuthanasiaMonkey.Tests.IntegrationTests
{
    public class EnvironmentVarsHelperTests
    {
        IEnvironmentVarsHelper helper;
        IDictionary<string, string> originalVars;

        public EnvironmentVarsHelperTests()
        {
            helper = new EnvironmentVarsHelper();
            originalVars = new Dictionary<string, string>();
        }

        [Fact]
        public void CorrectlyConvertsCommaSeparatedImmunities()
        {
            IEnumerable<string> expected = (new string[] { "keeper", "donoteuthanise" }).ToList();
            string testValue = "Keeper, DoNotEuthanise";
            EnvironmentVarsTest(EnvironmentVarsHelper.ImmunityTagsEnv, testValue, expected, () => helper.GetImmunities());
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("True", true)]
        [InlineData("TRUE", true)]
        [InlineData("TrUe", true)]
        [InlineData("1", true)]
        [InlineData("0", true)]
        [InlineData("-1", true)]
        [InlineData("false", false)]
        [InlineData("False", false)]
        [InlineData("FALSE", false)]
        [InlineData("FaLsE", false)]
        public void CorrectlyParsesBooleans(string testValue, bool expected)
        {
            EnvironmentVarsTest(EnvironmentVarsHelper.DryRunEnv, testValue, expected, () => helper.IsDryRun);
        }

        [Fact]
        public void DryRunIsTrueIfEnvironmentVarNotPresent()
        {
            const string dryRunEnv = EnvironmentVarsHelper.DryRunEnv;

            InitialiseEnvironment(dryRunEnv, null);

            Assert.Null(Environment.GetEnvironmentVariable(dryRunEnv));

            bool actual = helper.IsDryRun;

            Assert.Equal(true, actual);
        }

        [Theory]
        [InlineData("5", 5, "sensible setting")]
        [InlineData(null, EnvironmentVarsHelper.MaxAgeIfNotSpecified, "returns default if var not set")]
        [InlineData("", EnvironmentVarsHelper.MaxAgeIfNotSpecified, "returns default if var empty")]
        [InlineData("five", EnvironmentVarsHelper.MaxAgeIfNotSpecified, "returns default if value not parseable")]
        [InlineData("10", 10, "long setting")]
        [InlineData("1", 1, "danger mode")]
        public void MaxAgeCorrectlyParsed(string value, int expectedMaxAge, string caseMessage)
        {
            const string maxAgeEnv = EnvironmentVarsHelper.MaxAgeEnv;

            InitialiseEnvironment(maxAgeEnv, value);

            //if an Environment Variable is set to an empty string, that's the same as removing it
            Assert.Equal(value != string.Empty ? value: null, Environment.GetEnvironmentVariable(maxAgeEnv));

            DateTime expected = DateTime.UtcNow.AddDays(-expectedMaxAge);

            DateTime actual = helper.GetMaxAge();

            CompareDates(expected, actual, caseMessage, precisionInSeconds: 2);

            ResetEnvironment();
        }

        private static void CompareDates(DateTime expected, DateTime actual, string caseMessage, int precisionInSeconds)
        {
            var acceptableDifference = TimeSpan.FromSeconds(precisionInSeconds);
            TimeSpan difference = actual - expected;
            Assert.True(difference < acceptableDifference, caseMessage);
        }

        private void EnvironmentVarsTest<T>(string name, string testValue, T expected, Func<T> methodToTest)
        {
            InitialiseEnvironment(name, testValue);

            var actual = methodToTest();
            Assert.Equal(expected, actual);

            ResetEnvironment();
        }

        private void InitialiseEnvironment(string name, string testValue)
        {
            string originalValue = Environment.GetEnvironmentVariable(name);
            originalVars.Add(name, originalValue);
            Environment.SetEnvironmentVariable(name, testValue);
        }

        private void ResetEnvironment()
        {
            foreach (var item in originalVars)
            {
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }
    }
}
