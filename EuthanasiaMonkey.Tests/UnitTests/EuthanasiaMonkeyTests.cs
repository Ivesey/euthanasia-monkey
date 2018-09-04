using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization;
using Xunit;
using Amazon.EC2.Model;
using Amazon.EC2;
using Moq;
using System.Threading;
using EuthanasiaMonkey;
using Amazon.Lambda.TestUtilities;
using System.Text;

namespace EuthanasiaMonkey.Tests.UnitTests
{
    public class EuthanasiaMonkeyTests
    {
        private const string FakeImmunities = "Keeper, DoNotEuthanise";
        IAmazonEC2 client;
        Mock<IAmazonEC2> mockEC2;
        IEnvironmentVarsHelper helper;
        Function sut;
        StringBuilder logBuffer;
        ILambdaContext context;
        Dictionary<string, object> request;

        public EuthanasiaMonkeyTests()
        {
            mockEC2 = new Mock<IAmazonEC2>(MockBehavior.Strict);
            client = mockEC2.Object;
            sut = new Function();
            helper = SetupMockEnvHelper();
            context = new TestLambdaContext();
            logBuffer = (context.Logger as TestLambdaLogger).Buffer;
            request = FakeCloudWatchEventRequest;
        }

        [Fact]
        public void InvokesDescribeInstances()
        {
            var fakeResponse = Task.Run(() => new DescribeInstancesResponse());

            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                It.IsAny<CancellationToken>())).
                Returns(fakeResponse);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            mockEC2.Verify(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                It.IsAny<CancellationToken>()));
            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(), 
                It.IsAny<CancellationToken>()), Times.Never);
            Assert.Contains("Nothing to euthanise", logBuffer.ToString());
        }

        [Fact]
        public void WhenThereIsAVictimDryRunPreventsEuthanisation()
        {
            var fakeResponse = Task.Run(() => DescribeInstancesResponseWithVictims);

            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                It.IsAny<CancellationToken>())).
                Returns(fakeResponse);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            mockEC2.Verify(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                It.IsAny<CancellationToken>()));
            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);

            Assert.Contains("I would have terminated i-amavictim if this had been for real...", logBuffer.ToString());
            Assert.DoesNotContain("i-shouldnotbevictimised", logBuffer.ToString());
        }

        [Fact]
        public void WhenThereIsAVictimItGetsEuthanised()
        {
            var fakeResponse = Task.Run(() => DescribeInstancesResponseWithVictims);
            var fakeTerminateResponse = Task.Run(() => new TerminateInstancesResponse());

            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                It.IsAny<CancellationToken>())).
                Returns(fakeResponse);
            mockEC2.Setup(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(), 
                It.IsAny<CancellationToken>())).
                Returns(fakeTerminateResponse);

            helper = SetupMockEnvHelper(dryRun: false);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.Is<TerminateInstancesRequest>(tir => 
                    tir.InstanceIds.Count == 1 && tir.InstanceIds[0].Equals("i-amavictim")),
                It.IsAny<CancellationToken>()));
            Assert.Contains("I'm terminating i-amavictim", logBuffer.ToString());
            Assert.DoesNotContain("i-shouldnotbevictimised", logBuffer.ToString());
        }

        [Fact]
        public void ImmunitiesAreRespected()
        {
            var fakeResponse = Task.Run(() => DescribeInstancesResponseWithImmuneVictims);

            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(), 
                default(CancellationToken))).
                Returns(fakeResponse);

            helper = SetupMockEnvHelper(dryRun: false);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            //not strictly necessary as MockBehaviour.Strict mode will error if it IS called...
            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);
            Assert.Contains("Nothing to euthanise", logBuffer.ToString());
        }

        [Fact]
        public void IgnoresNonRunningInstances()
        {
            var fakeResponse = Task.Run(() => new DescribeInstancesResponse());

            var expectedRequest = new DescribeInstancesRequest
            {
                Filters = new List<Filter>
                 {
                     new Filter { Name="instance-state-name", Values= { "running" } }
                 }
            };
            //var er = expectedRequest;

            mockEC2.Setup(c => c.DescribeInstancesAsync(It.Is<DescribeInstancesRequest>(
                dir => FiltersAreEquivalenet(dir.Filters, expectedRequest.Filters)),
                default(CancellationToken))).Returns(fakeResponse);

            helper = SetupMockEnvHelper(dryRun: false);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);
            Assert.Contains("Nothing to euthanise", logBuffer.ToString());
        }

        [Fact]
        public void CallsDescribeInstancesTwiceIfNecessary()
        {
            var fakePagedResponse = Task.Run(() => DescribeInstancesReponseWithPaging);
            var fakeFinalResponse = Task.Run(() => DescribeInstancesResponseWithVictims);
            var fakeTerminateResponse = Task.Run(() => new TerminateInstancesResponse());

            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>())).
                    Returns(fakePagedResponse);
            mockEC2.Setup(c => c.DescribeInstancesAsync(
                It.Is<DescribeInstancesRequest>(dir => dir.NextToken == "DummyToken"),
                It.IsAny<CancellationToken>())).
                    Returns(fakeFinalResponse);
            mockEC2.Setup(c => c.TerminateInstancesAsync(
                It.IsAny<TerminateInstancesRequest>(),
                It.IsAny<CancellationToken>())).
                    Returns(fakeTerminateResponse);

            helper = SetupMockEnvHelper(dryRun: false);

            sut.EC2 = client;
            sut.Env = helper;

            var t = sut.FunctionHandler(request, context);
            t.Wait();

            mockEC2.Verify(c => c.DescribeInstancesAsync(
                It.IsAny<DescribeInstancesRequest>(),
                It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            mockEC2.Verify(c => c.TerminateInstancesAsync(
                It.Is<TerminateInstancesRequest>(tir => 
                    tir.InstanceIds.Count == 1 && 
                    tir.InstanceIds[0].Equals("i-amavictim")),
                It.IsAny<CancellationToken>()));
            Assert.Contains("I'm terminating i-amavictim", logBuffer.ToString());
            Assert.DoesNotContain("i-shouldnotbevictimised", logBuffer.ToString());
        }

        private static IEnvironmentVarsHelper SetupMockEnvHelper(bool dryRun = true)
        {
            IEnvironmentVarsHelper helper = Mock.Of<IEnvironmentVarsHelper>();
            var mockEnv = Mock.Get(helper);
            mockEnv.Setup(h => h.IsDryRun).Returns(dryRun);
            mockEnv.Setup(h => h.GetMaxAge()).Returns(MockMaxAge);
            mockEnv.Setup(h => h.GetImmunities()).Returns(FakeListOfEnvironmentVars);
            return helper;
        }

        private static readonly DateTime MockMaxAge = DateTime.Today.AddDays(-7);

        private static IEnumerable<string> FakeListOfEnvironmentVars
        {
            get
            {
                var stringValue = FakeImmunities;
                var qry = from t in stringValue.Split(',')
                          select t.Trim().ToLower();
                return qry.ToList();
            }
        }

        private static Dictionary<string, object> FakeCloudWatchEventRequest
        {
            get
            {
                return new Dictionary<string, object>();
            }
        }

        private static TerminateInstancesRequest ExpectedTerminateRequest
        {
            get
            {
                return new TerminateInstancesRequest
                {
                    InstanceIds = new List<string> { "i-amavictim" }
                };
            }
        }

        private static DescribeInstancesResponse DescribeInstancesResponseWithVictims
        {
            get
            {
                return CreateResponseWithVictims(withImmunities: false);
            }
        }

        private static DescribeInstancesResponse DescribeInstancesResponseWithImmuneVictims
        {
            get
            {
                return CreateResponseWithVictims(withImmunities: true);
            }
        }

        private static DescribeInstancesResponse CreateResponseWithVictims(bool withImmunities)
        {
            var tags = new List<Tag>();
            if (withImmunities)
            {
                tags.Add(new Tag { Key = "Keeper", Value = "true" });
            }
            else
            {
                tags.Add(new Tag { Key = "Environment", Value = "Prod" });
            }
            return new DescribeInstancesResponse
            {
                Reservations = new List<Reservation>
                {
                    new Reservation
                    {
                        Instances = new List<Instance>
                        {
                            new Instance
                            {
                                LaunchTime = MockMaxAge.AddDays(-1),
                                InstanceId = "i-amavictim",
                                Tags = tags
                            },
                            new Instance
                            {
                                LaunchTime = MockMaxAge.AddDays(1),
                                InstanceId = "i-shouldnotbevictimised",
                            }
                        }
                    }
                }
            };
        }

        private static DescribeInstancesResponse DescribeInstancesReponseWithPaging
        {
            get
            {
                return new DescribeInstancesResponse
                {
                    Reservations = new List<Reservation>
                    {
                        new Reservation
                        {
                            Instances = new List<Instance>
                            {
                                new Instance
                                {
                                    LaunchTime = MockMaxAge.AddDays(1),
                                    InstanceId = "i-shouldnotbevictimised",
                                }
                            }
                        }
                    },
                    NextToken = "DummyToken"
                };
            }
        }

        private static bool FiltersAreEquivalenet(List<Filter> x, List<Filter> y)
        {
            if (x.Count != y.Count)
            {
                return false;
            }
            var comp = new FilterComparer();
            return y.TrueForAll(filter => y.Contains(filter, comp));
        }

        private class FilterComparer : IEqualityComparer<Filter>
        {
            public bool Equals(Filter x, Filter y)
            {
                if (!x.Name.Equals(y.Name))
                {
                    return false;
                }
                return x.Values.TrueForAll(v => y.Values.Contains(v));
            }

            public int GetHashCode(Filter obj)
            {
                return $"{obj.Name}-{string.Join("-", obj.Values)}".GetHashCode();
            }
        }
    }
}
