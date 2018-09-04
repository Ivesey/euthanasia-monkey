using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization;
using Amazon.EC2;
using Amazon.EC2.Model;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace EuthanasiaMonkey
{
    public class Function
    {
        public async Task FunctionHandler(Dictionary<string, object> input, ILambdaContext context)
        {
            var req = new DescribeInstancesRequest
            {
                Filters = new List<Filter>
                 {
                     new Filter { Name="instance-state-name", Values= { "running" } }
                 }
            };

            var victims = new List<string>();
            DescribeInstancesResponse response;
            var immunities = Env.GetImmunities();

            DateTime maxAge = Env.GetMaxAge();

            do
            {
                response = await EC2.DescribeInstancesAsync(req);

                foreach (var res in response.Reservations)
                {
                    var newVictims = from i in res.Instances
                                     where i.LaunchTime < maxAge && i.Tags.Count(t => immunities.Contains(t.Key.ToLower())) == 0
                                     select i.InstanceId;
                    victims.AddRange(newVictims);
                }

                req.NextToken = response.NextToken;

            } while (!string.IsNullOrEmpty(response.NextToken));


            if (victims.Count > 0)
            {
                if (Env.IsDryRun)
                {
                    context.Logger.LogLine($"I would have terminated {string.Join(", ", victims)} if this had been for real...");
                }
                else
                {
                    if (Env.IgnoreApiTerminationProtection)
                    {
                        Parallel.ForEach(victims, async v =>
                        {
                            await ec2.ModifyInstanceAttributeAsync(new ModifyInstanceAttributeRequest
                            {
                                DisableApiTermination = false,
                                InstanceId = v
                            });
                        });
                    }

                    var terminator = new TerminateInstancesRequest
                    {
                        InstanceIds = victims
                    };
                    context.Logger.LogLine($"I'm terminating {string.Join(", ", victims)}.");
                    await EC2.TerminateInstancesAsync(terminator);
                }
            }
            else
            {
                context.Logger.LogLine("Nothing to euthanise");
            }
        }



        /// <summary>
        /// property injection
        /// </summary>
        public IAmazonEC2 EC2
        {
            get
            {
                if (ec2 == null)
                {
                    ec2 = new AmazonEC2Client();
                }
                return ec2;
            }

            set { ec2 = value; }
        }
        private IAmazonEC2 ec2;


        public IEnvironmentVarsHelper Env
        {
            get
            {
                if (env == null)
                {
                    env = new EnvironmentVarsHelper();
                }
                return env;
            }
            set
            {
                env = value;
            }
        }
        private IEnvironmentVarsHelper env;

    }
}
