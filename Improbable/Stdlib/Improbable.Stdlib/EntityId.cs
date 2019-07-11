using System;

namespace Improbable.Stdlib
{
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public EntityId(long id)
        {
            Value = id;
        }

        public long Value { get; }

        public bool Equals(EntityId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is EntityId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static implicit operator EntityId(long value)
        {
            return new EntityId(value);
        }

        public static bool operator ==(EntityId lhs, EntityId rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(EntityId lhs, EntityId rhs)
        {
            return !(lhs == rhs);
        }

        public bool IsValid => Value > 0;

        public override string ToString()
        {
            return IsValid ? Value.ToString() : "Invalid EntityId";
        }

        public int CompareTo(EntityId other)
        {
            return Value.CompareTo(other.Value);
        }
    }
}
