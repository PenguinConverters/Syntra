// -----------------------------------------------------------------------
// <copyright file="QuickDictionaryTests.cs" company="Penguin Converters AG">
//     Copyright (c) Penguin Converters AG. All rights reserved.
// </copyright>
// <author>Syntra Team</author>
// -----------------------------------------------------------------------

using System.Collections;
using PenguinConverters.Syntra.Core.Types;

namespace PenguinConverters.Syntra.Core.Tests.Types;

[TestFixture]
public class QuickDictionaryTests
{
    #region Methods

    [Test]
    public void Default_UsesOrdinalComparer()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        dictionary.Add("Key", "value");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Comparer, Is.SameAs(StringComparer.Ordinal));
            Assert.That(dictionary.ContainsKey("Key"), Is.True);
            Assert.That(dictionary.ContainsKey("key"), Is.False);
        });
    }

    [Test]
    public void OrdinalComparer_ExplicitlyPassed_UsesOrdinalFastPath()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(StringComparer.Ordinal);

        //Act
        dictionary.Add("Key", "value");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Comparer, Is.SameAs(StringComparer.Ordinal));
            Assert.That(dictionary.ContainsKey("key"), Is.False);
        });
    }

    [Test]
    public void OrdinalIgnoreCaseComparer_MatchesRegardlessOfCase()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        //Act
        dictionary.Add("SamAccountName", "jdoe");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Comparer, Is.SameAs(StringComparer.OrdinalIgnoreCase));
            Assert.That(dictionary.ContainsKey("samaccountname"), Is.True);
            Assert.That(dictionary["SAMACCOUNTNAME"], Is.EqualTo("jdoe"));
        });
    }

    [Test]
    public void OrdinalIgnoreCase_SetThroughIndexer_OverwritesRegardlessOfCase()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(StringComparer.OrdinalIgnoreCase);
        dictionary.Add("Mail", "first@example.com");

        //Act
        dictionary["MAIL"] = "second@example.com";

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Count, Is.EqualTo(1));
            Assert.That(dictionary["mail"], Is.EqualTo("second@example.com"));
        });
    }

    [Test]
    public void CustomComparer_IsUsedForLookup()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(StringComparer.InvariantCultureIgnoreCase);

        //Act
        dictionary.Add("Key", "value");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Comparer, Is.SameAs(StringComparer.InvariantCultureIgnoreCase));
            Assert.That(dictionary.ContainsKey("KEY"), Is.True);
        });
    }

    [Test]
    public void Enumeration_PreservesInsertionOrder()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("zebra", 1);
        dictionary.Add("apple", 2);
        dictionary.Add("mango", 3);

        //Act
        List<string> keys = new List<string>();
        foreach (KeyValuePair<string, object?> pair in dictionary)
            keys.Add(pair.Key);

        //Assert
        Assert.That(keys, Is.EqualTo(new[] { "zebra", "apple", "mango" }));
    }

    [Test]
    public void Add_BeyondDefaultCapacity_GrowsAndKeepsAllEntries()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        for (int i = 0; i < 50; i++)
            dictionary.Add($"key{i}", i);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Count, Is.EqualTo(50));
            Assert.That(dictionary.Capacity, Is.GreaterThanOrEqualTo(50));
            for (int i = 0; i < 50; i++)
                Assert.That(dictionary[$"key{i}"], Is.EqualTo(i));
        });
    }

    [Test]
    public void DefaultConstructor_AllocatesBackingArrayOnFirstInsertOnly()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        int capacityWhileEmpty = dictionary.Capacity;

        //Act
        dictionary.Add("first", 1);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(capacityWhileEmpty, Is.Zero);
            Assert.That(dictionary.Capacity, Is.GreaterThan(0));
            Assert.That(dictionary.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("key", 1);

        //Act
        Action action = () => dictionary.Add("key", 2);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void TryAdd_ExistingKey_ReturnsFalseAndKeepsOriginal()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("key", 1);

        //Act
        bool added = dictionary.TryAdd("key", 2);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(added, Is.False);
            Assert.That(dictionary["key"], Is.EqualTo(1));
        });
    }

    [Test]
    public void NullValue_IsStoredAndRetrieved()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        dictionary.Add("key", null);
        bool found = dictionary.TryGetValue("key", out object? value);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.Null);
            Assert.That(dictionary.ContainsKey("key"), Is.True);
            Assert.That(dictionary.ContainsValue(null), Is.True);
        });
    }

    [Test]
    public void NullKey_ThrowsArgumentNullException()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        Action action = () => dictionary.Add(null!, 1);

        //Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Test]
    public void Indexer_MissingKey_ThrowsKeyNotFoundException()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("present", 1);

        //Act
        Action action = () => _ = dictionary["absent"];

        //Assert
        Assert.Throws<KeyNotFoundException>(action);
    }

    [Test]
    public void TryGetValue_MissingKey_ReturnsFalse()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("present", 1);

        //Act
        bool found = dictionary.TryGetValue("absent", out object? value);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(value, Is.Null);
        });
    }

    [Test]
    public void GetValueOrDefault_MissingKey_ReturnsDefault()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        object? value = dictionary.GetValueOrDefault("absent", "fallback");

        //Assert
        Assert.That(value, Is.EqualTo("fallback"));
    }

    [Test]
    public void Remove_FromMiddle_PreservesOrderOfRemaining()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("b", 2);
        dictionary.Add("c", 3);

        //Act
        bool removed = dictionary.Remove("b");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(dictionary.Count, Is.EqualTo(2));
            Assert.That(dictionary.Keys, Is.EqualTo(new[] { "a", "c" }));
            Assert.That(dictionary.ContainsKey("b"), Is.False);
        });
    }

    [Test]
    public void Remove_MissingKey_ReturnsFalse()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);

        //Act
        bool removed = dictionary.Remove("absent");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.False);
            Assert.That(dictionary.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Remove_ThenAdd_ReusesSlotAndAppendsAtEnd()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("b", 2);

        //Act
        dictionary.Remove("a");
        dictionary.Add("c", 3);

        //Assert
        Assert.That(dictionary.Keys, Is.EqualTo(new[] { "b", "c" }));
    }

    [Test]
    public void RemovePair_WrongValue_DoesNotRemove()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);

        //Act
        bool removed = dictionary.Remove(new KeyValuePair<string, object?>("a", 999));

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.False);
            Assert.That(dictionary.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clear_EmptiesDictionary()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("b", 2);

        //Act
        dictionary.Clear();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Count, Is.Zero);
            Assert.That(dictionary.ContainsKey("a"), Is.False);
        });
    }

    [Test]
    public void TrimExcess_ShrinksCapacityToCount()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(64);
        dictionary.Add("a", 1);

        //Act
        dictionary.TrimExcess();

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Capacity, Is.EqualTo(1));
            Assert.That(dictionary["a"], Is.EqualTo(1));
        });
    }

    [Test]
    public void EnsureCapacity_GrowsAndReturnsCapacity()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();

        //Act
        int capacity = dictionary.EnsureCapacity(32);

        //Assert
        Assert.That(capacity, Is.GreaterThanOrEqualTo(32));
    }

    [Test]
    public void CopyTo_WritesAllPairsAtOffset()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("b", 2);
        KeyValuePair<string, object?>[] target = new KeyValuePair<string, object?>[3];

        //Act
        dictionary.CopyTo(target, 1);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(target[0].Key, Is.Null);
            Assert.That(target[1].Key, Is.EqualTo("a"));
            Assert.That(target[2].Key, Is.EqualTo("b"));
        });
    }

    [Test]
    public void CopyTo_TargetTooSmall_ThrowsArgumentException()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);
        dictionary.Add("b", 2);
        KeyValuePair<string, object?>[] target = new KeyValuePair<string, object?>[1];

        //Act
        Action action = () => dictionary.CopyTo(target, 0);

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void Enumerator_AfterMutation_ThrowsInvalidOperationException()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);

        //Act
        Action action = () =>
        {
            foreach (KeyValuePair<string, object?> pair in dictionary)
                dictionary.Add($"new{pair.Key}", 2);
        };

        //Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Test]
    public void Keys_And_Values_MatchInsertionOrder()
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 10);
        dictionary.Add("b", 20);

        //Act
        List<string> keys = new List<string>(dictionary.Keys);
        List<object?> values = new List<object?>(dictionary.Values);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(keys, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(values, Is.EqualTo(new object[] { 10, 20 }));
        });
    }

    [Test]
    public void CollectionInitializerConstructor_CopiesAllEntries()
    {
        //Arrange
        List<KeyValuePair<string, object?>> source = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("a", 1),
            new KeyValuePair<string, object?>("b", 2)
        };

        //Act
        QuickDictionary dictionary = new QuickDictionary(source);

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Count, Is.EqualTo(2));
            Assert.That(dictionary["a"], Is.EqualTo(1));
            Assert.That(dictionary["b"], Is.EqualTo(2));
        });
    }

    [Test]
    public void UsedThroughIDictionaryInterface_BehavesAsDictionary()
    {
        //Arrange
        IDictionary<string, object?> dictionary = new QuickDictionary(StringComparer.OrdinalIgnoreCase);

        //Act
        dictionary["Key"] = "value";

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary.Count, Is.EqualTo(1));
            Assert.That(dictionary.ContainsKey("KEY"), Is.True);
            Assert.That(dictionary.Keys, Is.EqualTo(new[] { "Key" }));
            Assert.That(dictionary.IsReadOnly, Is.False);
        });
    }

    [Test]
    public void NonGenericIDictionary_SupportsAddAndLookup()
    {
        //Arrange
        IDictionary dictionary = new QuickDictionary();

        //Act
        dictionary.Add("key", "value");

        //Assert
        Assert.Multiple(() =>
        {
            Assert.That(dictionary["key"], Is.EqualTo("value"));
            Assert.That(dictionary.Contains("key"), Is.True);
            Assert.That(dictionary["absent"], Is.Null);
            Assert.That(dictionary.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void NonGenericIDictionary_NonStringKey_ThrowsArgumentException()
    {
        //Arrange
        IDictionary dictionary = new QuickDictionary();

        //Act
        Action action = () => dictionary.Add(42, "value");

        //Assert
        Assert.Throws<ArgumentException>(action);
    }

    [Test]
    public void NonGenericEnumerator_YieldsDictionaryEntries()
    {
        //Arrange
        IDictionary dictionary = new QuickDictionary();
        dictionary.Add("a", 1);

        //Act
        List<object?> keys = new List<object?>();
        IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
            keys.Add(enumerator.Key);

        //Assert
        Assert.That(keys, Is.EqualTo(new object[] { "a" }));
    }

    [Test]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        //Arrange

        //Act
        Action action = () => new QuickDictionary(-1);

        //Assert
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [TestCase(1)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(33)]
    public void LookupAcrossGrowthBoundaries_FindsEveryKey(int entryCount)
    {
        //Arrange
        QuickDictionary dictionary = new QuickDictionary(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entryCount; i++)
            dictionary.Add($"Attribute{i}", i);

        //Act
        int found = 0;
        for (int i = 0; i < entryCount; i++)
            if (dictionary.ContainsKey($"attribute{i}"))
                found++;

        //Assert
        Assert.That(found, Is.EqualTo(entryCount));
    }

    #endregion
}
