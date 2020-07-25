using Autofac;
using DShop.Common;
using DShop.Common.Handlers;
using DShop.Common.Jaeger;
using DShop.Common.Messages;
using DShop.Common.RabbitMq;
using DShop.CrossCutting.MultiTenant;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using OpenTracing;
using RawRabbit;
using RawRabbit.Common;
using RawRabbit.Configuration;
using RawRabbit.Enrichers.MessageContext;
using RawRabbit.Instantiation;
using RawRabbit.Pipe;
using RawRabbit.Pipe.Middleware;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DsLine.Core.RabbitMQ
{

    public static class Extensions
    {
        public static IBusSubscriber UseRabbitMq(this IApplicationBuilder app, string tenant)
        {
            return new BusSubscriber(app, tenant);
        }


        public static void AddRabbitMq(this ContainerBuilder builder)
        {
            builder.Register(context =>
            {
                var configuration = context.Resolve<IConfiguration>();
                var options = configuration.GetOptions<List<RabbitMqOptions>>("ListRabbitMq");

                return options;
            }).SingleInstance();

            var assembly = Assembly.GetCallingAssembly();
            builder.RegisterAssemblyTypes(assembly)
                .AsClosedTypesOf(typeof(IEventHandler<>))
                .InstancePerDependency();
            builder.RegisterAssemblyTypes(assembly)
                .AsClosedTypesOf(typeof(ICommandHandler<>))
                .InstancePerDependency();
            builder.RegisterType<Handler>().As<IHandler>()
                .InstancePerDependency();
            builder.RegisterType<BusPublisher>().As<IBusPublisher>()
                .InstancePerDependency();
            builder.RegisterInstance(DShopDefaultTracer.Create()).As<ITracer>().SingleInstance()
                .PreserveExistingDefaults();

            ConfigureBus(builder);
        }

        private static void ConfigureBus(ContainerBuilder builder)
        {
            builder.Register(context =>
            {
                List<IInstanceFactory> instanceFactories = new List<IInstanceFactory>();

                var listoptions = context.Resolve<List<RabbitMqOptions>>();
                var tracer = context.Resolve<ITracer>();

                if (listoptions != null)
                {

                    foreach (var item in listoptions)
                    {
                        IInstanceFactory instanceFactory = RawRabbit.Instantiation.RawRabbitFactory.CreateInstanceFactory(new RawRabbitOptions
                        {
                            DependencyInjection = ioc =>
                            {
                                ioc.AddSingleton(item as RabbitMqOptions);
                                ioc.AddSingleton(item as RawRabbitConfiguration);
                                ioc.AddSingleton<INamingConventions>(new CustomNamingConventions(item.Namespace));
                                ioc.AddSingleton(tracer);


                                ioc.AddSingleton<ITenant, Tenant>(provider =>
                                {
                                    Tenant tenant1 = new Tenant();
                                    tenant1.TenantId = item.VirtualHost;
                                    return tenant1;
                                });

                                var tenant = context.Resolve<ITenant>();
                                tenant.TenantId = item.VirtualHost;
                                //var dbContext = context.Resolve<IBaseDbContext>();
                                //dbContext.RabbitMqcnnSQL = item.VirtualHost;
                                //ioc.AddSingleton(dbContext);
                            },
                            Plugins = p => p
                                .UseAttributeRouting()
                                .UseRetryLater()
                                .UpdateRetryInfo()
                                .UseMessageContext<CorrelationContext>()
                                .UseContextForwarding()
                                .UseJaeger(tracer)
                        });

                        instanceFactories.Add(instanceFactory);
                    }
                }

                return instanceFactories;
            }).SingleInstance();
            builder.Register(context =>
            {
                List<RawRabbit.IBusClient> busClients = new List<RawRabbit.IBusClient>();
                context.Resolve<List<IInstanceFactory>>().ForEach(x => busClients.Add(x.Create()));
                return busClients;
            });
        }

        private class CustomNamingConventions : NamingConventions
        {
            public CustomNamingConventions(string defaultNamespace)
            {
                var assemblyName = Assembly.GetEntryAssembly().GetName().Name;
                ExchangeNamingConvention = type => GetNamespace(type, defaultNamespace).ToLowerInvariant();
                RoutingKeyConvention = type =>
                    $"{GetRoutingKeyNamespace(type, defaultNamespace)}{type.Name.Underscore()}".ToLowerInvariant();
                QueueNamingConvention = type => GetQueueName(assemblyName, type, defaultNamespace);
                ErrorExchangeNamingConvention = () => $"{defaultNamespace}.error";
                RetryLaterExchangeConvention = span => $"{defaultNamespace}.retry";
                RetryLaterQueueNameConvetion = (exchange, span) =>
                    $"{defaultNamespace}.retry_for_{exchange.Replace(".", "_")}_in_{span.TotalMilliseconds}_ms".ToLowerInvariant();
            }

            private static string GetRoutingKeyNamespace(Type type, string defaultNamespace)
            {
                var @namespace = type.GetCustomAttribute<MessageNamespaceAttribute>()?.Namespace ?? defaultNamespace;

                return string.IsNullOrWhiteSpace(@namespace) ? string.Empty : $"{@namespace}.";
            }

            private static string GetNamespace(Type type, string defaultNamespace)
            {
                var @namespace = type.GetCustomAttribute<MessageNamespaceAttribute>()?.Namespace ?? defaultNamespace;

                return string.IsNullOrWhiteSpace(@namespace) ? type.Name.Underscore() : $"{@namespace}";
            }

            private static string GetQueueName(string assemblyName, Type type, string defaultNamespace)
            {
                var @namespace = type.GetCustomAttribute<MessageNamespaceAttribute>()?.Namespace ?? defaultNamespace;
                var separatedNamespace = string.IsNullOrWhiteSpace(@namespace) ? string.Empty : $"{@namespace}.";

                return $"{assemblyName}/{separatedNamespace}{type.Name.Underscore()}".ToLowerInvariant();
            }
        }

        private class RetryStagedMiddleware : StagedMiddleware
        {
            public override string StageMarker { get; } = RawRabbit.Pipe.StageMarker.MessageDeserialized;

            public override async Task InvokeAsync(IPipeContext context,
                CancellationToken token = new CancellationToken())
            {
                var retry = context.GetRetryInformation();
                if (context.GetMessageContext() is CorrelationContext message)
                {
                    message.Retries = retry.NumberOfRetries;
                }

                await Next.InvokeAsync(context, token);
            }
        }

        private static IClientBuilder UpdateRetryInfo(this IClientBuilder clientBuilder)
        {
            clientBuilder.Register(c => c.Use<RetryStagedMiddleware>());
            return clientBuilder;
        }
    }
}