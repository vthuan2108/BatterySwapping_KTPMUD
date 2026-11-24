using System.Text.Json;
using BatterySwapping.Contracts;
using BatterySwapping.Core;
using MQTTnet;
using MQTTnet.Protocol;

namespace BatterySwapping.Server;
public sealed class MqttWorker(BssEngine engine,IConfiguration config,ILogger<MqttWorker> log):BackgroundService
{
    public bool Connected {get;private set;}
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var factory=new MqttClientFactory();using var client=factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e=>{
            try
            {
                var m=JsonSerializer.Deserialize<DeviceMessage>(e.ApplicationMessage.ConvertPayloadToString(),SqlStore.Json);
                if(m==null || e.ApplicationMessage.Topic.Split('/').ElementAtOrDefault(2)!=m.StationId) return Task.CompletedTask;
                engine.Receive(m);
            }
            catch(Exception ex){log.LogWarning("Rejected device message on {Topic}: {Reason}",e.ApplicationMessage.Topic,ex.Message);}
            return Task.CompletedTask;
        };
        var options=new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1",18883).WithClientId("bss-backend")
            .WithCredentials("backend",config["Broker:Password"]??throw new InvalidOperationException("Broker password missing"))
            .WithCleanSession().Build();
        while(!stop.IsCancellationRequested)
        {
            try
            {
                if(!client.IsConnected)
                {
                    await client.ConnectAsync(options,stop);
                    var sub=factory.CreateSubscribeOptionsBuilder();
                    foreach(var topic in new[]{"telemetry","alerts","events","acks"}) sub.WithTopicFilter(t=>t.WithTopic($"bss/station/+/{topic}").WithAtLeastOnceQoS());
                    await client.SubscribeAsync(sub.Build(),stop);
                    log.LogInformation("Local MQTT connected");
                }
                Connected=client.IsConnected;
                engine.Tick();
                var c=engine.ClaimCommand();
                if(c!=null)
                {
                    log.LogInformation("Dispatch {CommandId} {SessionId} {Command}",c.CommandId,c.SessionId,c.Command);
                    await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"bss/station/{c.StationId}/commands")
                        .WithPayload(BssEngine.Serialize(c)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(),stop);
                }
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
            catch(Exception ex){Connected=false;log.LogWarning("MQTT/runtime retry: {Reason}",ex.Message);}
            try {await Task.Delay(500,stop);}catch(OperationCanceledException){break;}
        }
        Connected=false;
    }
}
