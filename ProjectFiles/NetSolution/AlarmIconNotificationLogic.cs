#region Using directives
using System;
using CoreBase = FTOptix.CoreBase;
using FTOptix.HMIProject;
using UAManagedCore;
using System.Linq;
using UAManagedCore.Logging;
using FTOptix.NetLogic;
using FTOptix.DataLogger;
using FTOptix.Recipe;
using FTOptix.Report;
using FTOptix.UI;
using FTOptix.System;
using FTOptix.WebUI;
#endregion

public class AlarmIconNotificationLogic : BaseNetLogic
{
	public override void Start()
	{
		var context = LogicObject.Context;
		affinityId = context.AssignAffinityId();

		RegisterObserverOnLocalizedAlarmsContainer(context);
		RegisterObserverOnSessionLocaleIdChanged(context);
		RegisterObserverOnLocalizedAlarmsObject(context);
	}

	public override void Stop()
	{
		if (alarmEventRegistration != null)
			alarmEventRegistration.Dispose();
		if (alarmEventRegistration2 != null)
			alarmEventRegistration2.Dispose();

		alarmEventRegistration = null;
		alarmEventRegistration2 = null;
		alarmsNotificationObserver = null;
		retainedAlarmsObjectObserver = null;
	}

	public void RegisterObserverOnLocalizedAlarmsObject(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);

		retainedAlarmsObjectObserver = new RetainedAlarmsObjectObserver((ctx) => RegisterObserverOnLocalizedAlarmsContainer(ctx));

		// observe ReferenceAdded of localized alarm containers
		alarmEventRegistration2 = retainedAlarms.RegisterEventObserver(
			retainedAlarmsObjectObserver, EventType.ForwardReferenceAdded, affinityId);
	}

	public void RegisterObserverOnLocalizedAlarmsContainer(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
		var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
		var localizedAlarmsContainer = context.GetNode((NodeId)localizedAlarmsVariable.GetValue());

		if (alarmEventRegistration != null)
		{
			alarmEventRegistration.Dispose();
			alarmEventRegistration = null;
		}

		alarmsNotificationObserver = new AlarmsNotificationObserver(LogicObject);
		alarmsNotificationObserver.Initialize();

		alarmEventRegistration = localizedAlarmsContainer.RegisterEventObserver(
			alarmsNotificationObserver,
			EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved, affinityId);
	}

	public void RegisterObserverOnSessionLocaleIdChanged(IContext context)
	{
		var currentSessionLocaleIdVariable = context.Sessions.CurrentSessionInfo.SessionObject.Children["ActualLocaleId"];

		localeIdChangedObserver = new CallbackVariableChangeObserver((IUAVariable variable, UAValue newValue, UAValue oldValue, uint[] a, ulong aa) =>
		{
			RegisterObserverOnLocalizedAlarmsContainer(context);
		});

		localeIdRegistration = currentSessionLocaleIdVariable.RegisterEventObserver(
			localeIdChangedObserver, EventType.VariableValueChanged, affinityId);
	}

	private class RetainedAlarmsObjectObserver : IReferenceObserver
	{
		public RetainedAlarmsObjectObserver(Action<IContext> action)
		{
			registrationCallback = action;
		}

		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
            string localeId = targetNode.Context.Sessions.CurrentSessionHandler.ActualLocaleId;
            if (String.IsNullOrEmpty(localeId))
                localeId = "en-US";

			if (targetNode.BrowseName == localeId)
				registrationCallback(targetNode.Context);
		}

		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
		}

		private Action<IContext> registrationCallback;
	}

	private class AlarmsNotificationObserver : IReferenceObserver
	{
		public AlarmsNotificationObserver(IUANode logicNode)
		{
			this.logicNode = logicNode;
		}

		public void Initialize()
		{
			retainedAlarmsCount = logicNode.GetVariable("AlarmCount");
			lastAlarm = logicNode.GetVariable("LastAlarm");

			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			if (localizedAlarmsContainer == null)
			{
				retainedAlarmsCount.Value = 0;
				lastAlarm.Value = NodeId.Empty;
				return;
			}

			var children = localizedAlarmsContainer.Children.ToArray();
			retainedAlarmsCount.Value = children.Length;
			if (children.Any())
				lastAlarm.Value = children.Last()?.NodeId ?? NodeId.Empty;
			else
				lastAlarm.Value = NodeId.Empty;
		}

		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			++retainedAlarmsCount.Value;

			lastAlarm.Value = targetNode.NodeId;
		}

		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			if (localizedAlarmsContainer == null)
			{
				retainedAlarmsCount.Value = 0;
				lastAlarm.Value = NodeId.Empty;
				return;
			}

			var children = localizedAlarmsContainer.Children.ToArray();
			retainedAlarmsCount.Value = children.Length;
			if (children.Any())
				lastAlarm.Value = children.Last()?.NodeId ?? NodeId.Empty;
			else
				lastAlarm.Value = NodeId.Empty;
		}

		private IUAVariable retainedAlarmsCount;
		private IUAVariable lastAlarm;
		private IUANode logicNode;
	}

	private uint affinityId = 0;
	private AlarmsNotificationObserver alarmsNotificationObserver;
	private RetainedAlarmsObjectObserver retainedAlarmsObjectObserver;
	private IEventRegistration alarmEventRegistration;
	private IEventRegistration alarmEventRegistration2;
	private IEventRegistration localeIdRegistration;
	private IEventObserver localeIdChangedObserver;
}
