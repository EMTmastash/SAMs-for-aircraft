using System;
using System.Threading;
using System.Reflection;
using UnityEngine;
public class RadarSARHSeeker : MissileSeeker
{
	public override void Initialize(Unit target, GlobalPosition aimpoint)
	{
		Unit unit = null;
		if (this.missile.owner != null)
		{
			unit = this.missile.owner.radar.GetAttachedUnit();
		}
		this.topSpeed = this.missile.GetWeaponInfo().maxSpeed;
		if (unit != null)
		{
			this.radarSource = unit.radar as Radar;
			this.missile.radar = this.radarSource;
			this.radarParams = this.radarSource.RadarParameters;
			this.detectorUnit = this.missile.owner;
			if (this.radarSource != null)
			{
				this.radarSourcePoint = this.radarSource.GetScanPoint();
				this.detectorUnit = this.radarSource.GetAttachedUnit();
				this.radarSource.AddGuidedMissile(this.missile);
			}
			this.missile.onJam += this.SARHSeeker_OnJam;
			this.missile.onDisableUnit += this.SARHSeeker_OnDisable;
			this.targetUnit = target;
			if (target is IRadarReturn)
			{
				target.onAddRadarChaff += this.SARHSeeker_OnChaff;
			}
			GlobalPosition globalPosition;
			if (this.targetUnit != null && this.missile.NetworkHQ != null && this.missile.NetworkHQ.TryGetKnownPosition(this.targetUnit, out globalPosition))
			{
				this.knownPos = globalPosition;
				this.missile.SetAimpoint(this.knownPos, Vector3.zero);
			}
			this.lastTrackingCheck = Time.timeSinceLevelLoad;
			this.targetTransform = ((target != null) ? target.GetRandomPart() : null);
			if (this.proximityFuse && this.targetTransform != null)
			{
				this.missile.SetProxyFuse(this.targetTransform, target.rb);
			}
			if (this.guidanceDelay > 0f)
			{
				this.knownPos = this.missile.GlobalPosition() + this.missile.transform.forward * 10000f;
				this.missile.SetAimpoint(this.knownPos, Vector3.zero);
			}
			this.missile.DeployFins();
			this.StartSlowUpdateDelayed(1f, new Action(this.SlowChecks), default(CancellationToken));
			return;
		}
	}
	public override GlobalPosition GetEvasionPoint()
	{
		if (this.missile.radar != null)
		{
			return this.missile.radar.GetScanPoint().GlobalPosition();
		}
		return this.missile.GlobalPosition();
	}
	public override string GetSeekerType()
	{
		return "SARH";
	}
	private void SlowChecks()
	{
		if (this.missile.disabled)
		{
			return;
		}
		if (!this.missile.IsTangible() && this.missile.timeSinceSpawn > this.tangibleDelay)
		{
			this.missile.SetTangible(true);
			if (this.radarSource != null)
			{
				this.missile.RpcAssignRadar(this.radarSource.GetAttachedUnit());
			}
		}
		if (!this.missile.EngineOn() && this.missile.IsArmed() && (this.missile.LosingGround() || this.missile.MissedTarget() || this.missile.speed < this.selfDestructAtSpeed))
		{
			this.missile.Detonate(this.missile.rb.velocity, false, false);
		}
	}
	private void SARHSeeker_OnJam(Unit.JamEventArgs e)
	{
		if (Vector3.Angle(e.jammingUnit.GlobalPosition() - this.missile.GlobalPosition(), this.missile.transform.forward) > this.seekerAngle)
		{
			return;
		}
		this.jamAccumulation += e.jamAmount / Mathf.Max(this.jamTolerance, 0.1f);
		this.missile.RecordDamage(e.jammingUnit.persistentID, 0.01f);
	}
	private void SARHSeeker_OnChaff(RadarChaff source)
	{
		if (this.missile.targetID.NotValid)
		{
			return;
		}
		if (this.missile.seekerMode != Missile.SeekerMode.activeLock)
		{
			return;
		}
		float num = FastMath.Distance(base.transform.position, this.targetUnit.transform.position);
		float num2 = this.RangeCoef(num);
		Vector3 vector = FastMath.NormalizedDirection(base.transform.position, this.targetUnit.transform.position);
		Vector3 vector2 = FastMath.NormalizedDirection(source.transform.position, this.targetUnit.transform.position);
		float num3 = Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(vector, vector2)));
		float num4 = num2 * num3 / (1f + this.jamTolerance);
		this.jamAccumulation += num4;

	}
	private void SARHSeeker_OnDisable(Unit unit)
	{
		if (this.radarSource != null)
		{
			this.radarSource.RemoveGuidedMissile(this.missile);
		}
		this.missile.onJam -= this.SARHSeeker_OnJam;
		if (this.targetUnit != null)
		{
			this.targetUnit.onAddRadarChaff -= this.SARHSeeker_OnChaff;
		}
		this.missile.onDisableUnit -= this.SARHSeeker_OnDisable;
	}
	private float RangeCoef(float targetDistance)
	{
		float maxRange = this.radarParams.maxRange;
		float num = targetDistance / maxRange;
		return Mathf.Clamp01(1f - num);
	}
	private float GetTrackingStrength(Unit targetUnit)
	{
		if (Time.timeSinceLevelLoad - this.lastTrackingCheck < 0.2f)
		{
			return this.trackingStrength;
		}
		this.lastTrackingCheck = Time.timeSinceLevelLoad;
		if (!this.RadarSupportsTarget())
		{
			this.trackingStrength = 0f;
			return 0f;
		}
		if (!(targetUnit == null))
		{
			IRadarReturn radarReturn = targetUnit as IRadarReturn;
			if (radarReturn != null)
			{
				GlobalPosition globalPosition = this.radarSourcePoint.GlobalPosition();
				GlobalPosition globalPosition2 = targetUnit.GlobalPosition();
				Vector3 vector = globalPosition2 - globalPosition;
				vector.y = 0f;
				float num = FastMath.Distance(globalPosition, globalPosition2);
				float magnitude = vector.magnitude;
				float num2 = Mathf.Sqrt(12742000f * globalPosition.y);
				float num3 = Mathf.Sqrt(12742000f * globalPosition2.y);
				if (num2 + num3 <= num || !TargetCalc.LineOfSight(this.radarSourcePoint, targetUnit.transform, 10f))
				{
					this.trackingStrength = 0f;
					return 0f;
				}
				this.isJammed = this.jamAccumulation > this.jamTolerance || this.radarSource.IsJammed();
				if (this.isJammed || this.radarSource == null || this.radarSourcePoint == null || this.detectorUnit == null || this.detectorUnit.disabled)
				{
					this.trackingStrength = 0f;
					return 0f;
				}
				float num4 = 0f;
				if (magnitude < num2 && globalPosition2.y < globalPosition.y * (1f - magnitude / num2))
				{
					float num5 = num * targetUnit.radarAlt / (globalPosition.y - globalPosition2.y);
					num4 += Mathf.Min(num, 1000f) / num5;
				}
				num4 += targetUnit.maxRadius * targetUnit.maxRadius * 2f / (targetUnit.radarAlt * targetUnit.radarAlt);
				this.trackingStrength = radarReturn.GetRadarReturn(this.radarSourcePoint.position, null, this.missile, num, num4, this.radarParams, false);
				return this.trackingStrength;
			}
		}
		this.trackingStrength = 0f;
		return 0f;
	}
	private void SearchMode()
	{
		if (this.missile.seekerMode != Missile.SeekerMode.activeSearch)
		{
			this.missile.NetworkseekerMode = Missile.SeekerMode.activeSearch;
		}
		if (this.timeWithoutTrack > this.lockPersistence)
		{
			this.targetTransform = null;
			this.missile.SetTarget(null);
			this.missile.onJam -= this.SARHSeeker_OnJam;
			if (this.targetUnit != null)
			{
				this.targetUnit.onAddRadarChaff -= this.SARHSeeker_OnChaff;
			}
			return;
		}
		this.timeWithoutTrack += Time.fixedDeltaTime;
		this.knownPos += this.knownVel * Time.fixedDeltaTime;
		this.knownAccel = Vector3.zero;
	}
	private void LockedMode()
	{
		if (this.missile.seekerMode != Missile.SeekerMode.activeLock)
		{
			this.missile.NetworkseekerMode = Missile.SeekerMode.activeLock;
		}
		this.knownPos = this.targetTransform.GlobalPosition();
		this.knownVel = ((this.targetUnit.rb != null) ? this.targetUnit.rb.velocity : Vector3.zero);
		this.knownAccel = (this.knownVel - this.knownVelPrev) / Time.fixedDeltaTime;
		this.knownVelPrev = this.knownVel;
	}
	private void SendTargetInfo()
	{
		Vector3 vector = ((this.missile.timeSinceSpawn < 3f) ? (this.missile.transform.forward * this.topSpeed) : this.missile.rb.velocity);
		Vector3 vector2 = (this.missile.IsArmed() ? TargetCalc.GetLeadVectorWithAccel(this.knownPos, this.missile.GlobalPosition(), this.knownVel, vector, this.knownAccel, 10f) : Vector3.zero);
		GlobalPosition globalPosition = this.knownPos + vector2;
		globalPosition.y = Mathf.Max(globalPosition.y, 0f);
		this.missile.SetAimpoint(globalPosition, this.knownVel);
	}
	public override void Seek()
	{
		if (!this.missile.IsArmed() && this.missile.timeSinceSpawn > this.armDelay)
		{
			this.missile.Arm();
		}
		if (!this.guidance && this.missile.timeSinceSpawn > this.guidanceDelay)
		{
			this.guidance = true;
			GlobalPosition globalPosition;
			if (this.targetUnit != null && this.missile.NetworkHQ != null && this.missile.NetworkHQ.TryGetKnownPosition(this.targetUnit, out globalPosition))
			{
				this.knownPos = globalPosition;
			}
			return;
		}
		if (this.targetTransform != null && this.targetUnit != null)
		{
			this.jamAccumulation -= Mathf.Max(this.jamAccumulation, 0.2f) * Mathf.Max(this.jamTolerance, 0.1f) * Time.deltaTime;
			this.jamAccumulation = Mathf.Clamp01(this.jamAccumulation);
			if (this.GetTrackingStrength(this.targetUnit) >= this.radarParams.minSignal)
			{
				this.missile.SetTarget(this.targetUnit);
				this.timeWithoutTrack = 0f;
				this.LockedMode();
			}
			else
			{
				this.SearchMode();
			}
		}
		else
		{
			this.knownPos += this.knownVel * Time.fixedDeltaTime;
			if (this.targetUnit == null && this.missile.IsArmed())
			{
				this.missile.Detonate(this.missile.rb.velocity, false, false);
			}
		}
		this.SendTargetInfo();
	}
	private bool RadarSupportsTarget()
	{
		if (this.missile.owner == null || this.missile.owner.disabled || this.targetUnit == null || this.targetUnit.disabled || this.targetTransform == null || this.radarSource == null || this.radarSourcePoint == null || !this.radarSource.activated || !this.radarSource.IsOperational() || this.missile.owner.radar != this.radarSource || this.radarSource.GetAttachedUnit() != this.missile.owner)
		{
			return false;
		}
		IRadarReturn radarReturn = this.targetUnit as IRadarReturn;
		if (radarReturn == null || RadarConeField == null)
		{
			return false;
		}
		GlobalPosition globalPosition = this.radarSourcePoint.GlobalPosition();
		GlobalPosition globalPosition2 = this.targetUnit.GlobalPosition();
		float num = FastMath.Distance(globalPosition, globalPosition2);
		float num2 = (float)RadarConeField.GetValue(this.radarSource);
		if (num > this.radarSource.RadarParameters.maxRange * 2f || (num2 > 0f && Vector3.Angle(this.targetUnit.transform.position - this.radarSourcePoint.position, this.radarSourcePoint.forward) > num2))
		{
			return false;
		}
		return true;
	}

	private static readonly FieldInfo RadarConeField = typeof(Radar).GetField("radarCone", BindingFlags.Instance | BindingFlags.NonPublic);

	[SerializeField]
	private float lockPersistence = 2f;
	[SerializeField]
	private float tangibleDelay = 2f;
	[SerializeField]
	private float armDelay = 2f;
	[SerializeField]
	private float guidanceDelay;
	[SerializeField]
	private float selfDestructAtSpeed = 200f;
	[SerializeField]
	private float seekerAngle = 45f;
	private RadarParams radarParams;
	[Range(0f, 1f)]
	[SerializeField]
	private float jamTolerance;
	private Radar radarSource;
	private Transform radarSourcePoint;
	private Transform targetTransform;
	private GlobalPosition knownPos;
	private Vector3 knownVel;
	private Vector3 knownVelPrev;
	private Vector3 knownAccel;
	private float lastTrackingCheck;
	private float timeWithoutTrack;
	private float topSpeed;
	private float trackingStrength;
	private float jamAccumulation;
	private bool isJammed;
	private bool guidance;
	private Unit detectorUnit;
}

