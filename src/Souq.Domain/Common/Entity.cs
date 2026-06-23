namespace Souq.Domain.Common;

// اختصار شائع: معظم كياناتنا تستخدم int كمعرّف، فنوفّر صنفاً جاهزاً بذلك.
public abstract class Entity : BaseEntity<int> { }
