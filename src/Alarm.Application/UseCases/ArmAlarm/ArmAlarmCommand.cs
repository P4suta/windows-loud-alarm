using Alarm.Domain.Model;

namespace Alarm.Application.UseCases.ArmAlarm;

public sealed record ArmAlarmCommand(TimeOfDay Time, AudioSource Sound);
