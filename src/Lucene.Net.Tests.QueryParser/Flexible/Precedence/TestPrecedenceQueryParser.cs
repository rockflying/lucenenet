﻿using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Tokenattributes;
using Lucene.Net.Documents;
using Lucene.Net.QueryParsers.Flexible.Core;
using Lucene.Net.QueryParsers.Flexible.Standard.Config;
using Lucene.Net.QueryParsers.Flexible.Standard.Parser;
using Lucene.Net.Search;
using Lucene.Net.Support;
using Lucene.Net.Util;
using Lucene.Net.Util.Automaton;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lucene.Net.QueryParsers.Flexible.Precedence
{
    /// <summary>
    /// This test case tests {@link PrecedenceQueryParser}.
    /// <para/>
    /// It contains all tests from {@link QueryParserTestBase}
    /// with some adjusted to fit the precedence requirement, plus some precedence test cases.
    /// </summary>
    /// <seealso cref="QueryParserTestBase"/>
    //TODO: refactor this to actually extend that class, overriding the tests
    //that it adjusts to fit the precedence requirement, adding its extra tests.
    public class TestPrecedenceQueryParser : LuceneTestCase
    {
        public static Analyzer qpAnalyzer;

        [TestFixtureSetUp]
        public static void beforeClass()
        {
            qpAnalyzer = new QPTestAnalyzer();
        }

        [TestFixtureTearDown]
        public static void afterClass()
        {
            qpAnalyzer = null;
        }

        public sealed class QPTestFilter : TokenFilter
        {
            /**
             * Filter which discards the token 'stop' and which expands the token
             * 'phrase' into 'phrase1 phrase2'
             */
            public QPTestFilter(TokenStream @in)
                        : base(@in)
            {
                termAtt = AddAttribute<ICharTermAttribute>();
                offsetAtt = AddAttribute<IOffsetAttribute>();
            }

            private bool inPhrase = false;

            private int savedStart = 0;
            private int savedEnd = 0;

            private readonly ICharTermAttribute termAtt;

            private readonly IOffsetAttribute offsetAtt;


            public override bool IncrementToken()
            {
                if (inPhrase)
                {
                    inPhrase = false;
                    termAtt.SetEmpty().Append("phrase2");
                    offsetAtt.SetOffset(savedStart, savedEnd);
                    return true;
                }
                else
                    while (input.IncrementToken())
                        if (termAtt.toString().equals("phrase"))
                        {
                            inPhrase = true;
                            savedStart = offsetAtt.StartOffset();
                            savedEnd = offsetAtt.EndOffset();
                            termAtt.SetEmpty().Append("phrase1");
                            offsetAtt.SetOffset(savedStart, savedEnd);
                            return true;
                        }
                        else if (!termAtt.toString().equals("stop"))
                            return true;
                return false;
            }


            public override void Reset()
            {
                base.Reset();
                this.inPhrase = false;
                this.savedStart = 0;
                this.savedEnd = 0;
            }
        }

        public sealed class QPTestAnalyzer : Analyzer
        {

            /** Filters MockTokenizer with StopFilter. */

            public override sealed TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
            {
                Tokenizer tokenizer = new MockTokenizer(reader, MockTokenizer.SIMPLE, true);
                return new TokenStreamComponents(tokenizer, new QPTestFilter(tokenizer));
            }
        }

        private int originalMaxClauses;


        public override void SetUp()
        {
            base.SetUp();
            originalMaxClauses = BooleanQuery.MaxClauseCount;
        }

        public PrecedenceQueryParser GetParser(Analyzer a)
        {
            if (a == null)
                a = new MockAnalyzer(Random(), MockTokenizer.SIMPLE, true);
            PrecedenceQueryParser qp = new PrecedenceQueryParser();
            qp.Analyzer = (a);
            qp.SetDefaultOperator(/*StandardQueryConfigHandler.*/Operator.OR); // LUCENENET TODO: Change API back to the way it was..?
            return qp;
        }

        public Query GetQuery(string query, Analyzer a)
        {
            return (Query)GetParser(a).Parse(query, "field"); // LUCENENET TODO: There was no cast here in the original - perhaps object is the wrong type on the interface
        }

        public void assertQueryEquals(string query, Analyzer a, string result)
        {
            Query q = GetQuery(query, a);
            String s = q.ToString("field");
            if (!s.equals(result))
            {
                fail("Query /" + query + "/ yielded /" + s + "/, expecting /" + result
                    + "/");
            }
        }

        public void assertWildcardQueryEquals(String query, bool lowercase,
            String result)
        {
            PrecedenceQueryParser qp = GetParser(null);
            qp.LowercaseExpandedTerms = (lowercase);
            Query q = (Query)qp.Parse(query, "field");
            String s = q.ToString("field");
            if (!s.equals(result))
            {
                fail("WildcardQuery /" + query + "/ yielded /" + s + "/, expecting /"
                    + result + "/");
            }
        }

        public void assertWildcardQueryEquals(String query, String result)
        {
            PrecedenceQueryParser qp = GetParser(null);
            Query q = (Query)qp.Parse(query, "field");
            String s = q.ToString("field");
            if (!s.equals(result))
            {
                fail("WildcardQuery /" + query + "/ yielded /" + s + "/, expecting /"
                    + result + "/");
            }
        }

        public Query getQueryDOA(String query, Analyzer a)
        {
            if (a == null)
                a = new MockAnalyzer(Random(), MockTokenizer.SIMPLE, true);
            PrecedenceQueryParser qp = new PrecedenceQueryParser();
            qp.Analyzer = (a);
            qp.SetDefaultOperator(/*StandardQueryConfigHandler.*/Operator.AND);
            return (Query)qp.Parse(query, "field");
        }

        public void assertQueryEqualsDOA(String query, Analyzer a, String result)
        {
            Query q = getQueryDOA(query, a);
            String s = q.ToString("field");
            if (!s.equals(result))
            {
                fail("Query /" + query + "/ yielded /" + s + "/, expecting /" + result
                    + "/");
            }
        }

        [Test]
        public void testSimple()
        {
            assertQueryEquals("term term term", null, "term term term");
            assertQueryEquals("türm term term", null, "türm term term");
            assertQueryEquals("ümlaut", null, "ümlaut");

            assertQueryEquals("a AND b", null, "+a +b");
            assertQueryEquals("(a AND b)", null, "+a +b");
            assertQueryEquals("c OR (a AND b)", null, "c (+a +b)");
            assertQueryEquals("a AND NOT b", null, "+a -b");
            assertQueryEquals("a AND -b", null, "+a -b");
            assertQueryEquals("a AND !b", null, "+a -b");
            assertQueryEquals("a && b", null, "+a +b");
            assertQueryEquals("a && ! b", null, "+a -b");

            assertQueryEquals("a OR b", null, "a b");
            assertQueryEquals("a || b", null, "a b");

            assertQueryEquals("+term -term term", null, "+term -term term");
            assertQueryEquals("foo:term AND field:anotherTerm", null,
                "+foo:term +anotherterm");
            assertQueryEquals("term AND \"phrase phrase\"", null,
                "+term +\"phrase phrase\"");
            assertQueryEquals("\"hello there\"", null, "\"hello there\"");
            assertTrue(GetQuery("a AND b", null) is BooleanQuery);
            assertTrue(GetQuery("hello", null) is TermQuery);
            assertTrue(GetQuery("\"hello there\"", null) is PhraseQuery);

            assertQueryEquals("germ term^2.0", null, "germ term^2.0");
            assertQueryEquals("(term)^2.0", null, "term^2.0");
            assertQueryEquals("(germ term)^2.0", null, "(germ term)^2.0");
            assertQueryEquals("term^2.0", null, "term^2.0");
            assertQueryEquals("term^2", null, "term^2.0");
            assertQueryEquals("\"germ term\"^2.0", null, "\"germ term\"^2.0");
            assertQueryEquals("\"term germ\"^2", null, "\"term germ\"^2.0");

            assertQueryEquals("(foo OR bar) AND (baz OR boo)", null,
                "+(foo bar) +(baz boo)");
            assertQueryEquals("((a OR b) AND NOT c) OR d", null, "(+(a b) -c) d");
            assertQueryEquals("+(apple \"steve jobs\") -(foo bar baz)", null,
                "+(apple \"steve jobs\") -(foo bar baz)");
            assertQueryEquals("+title:(dog OR cat) -author:\"bob dole\"", null,
                "+(title:dog title:cat) -author:\"bob dole\"");

            PrecedenceQueryParser qp = new PrecedenceQueryParser();
            qp.Analyzer = (new MockAnalyzer(Random()));
            // make sure OR is the default:
            assertEquals(/*StandardQueryConfigHandler.*/Operator.OR, qp.GetDefaultOperator());
            qp.SetDefaultOperator(/*StandardQueryConfigHandler.*/ Operator.AND);
            assertEquals(/*StandardQueryConfigHandler.*/ Operator.AND, qp.GetDefaultOperator());
            qp.SetDefaultOperator(/*StandardQueryConfigHandler.*/ Operator.OR);
            assertEquals(/*StandardQueryConfigHandler.*/ Operator.OR, qp.GetDefaultOperator());

            assertQueryEquals("a OR !b", null, "a -b");
            assertQueryEquals("a OR ! b", null, "a -b");
            assertQueryEquals("a OR -b", null, "a -b");
        }

        [Test]
        public void testPunct()
        {
            Analyzer a = new MockAnalyzer(Random(), MockTokenizer.WHITESPACE, false);
            assertQueryEquals("a&b", a, "a&b");
            assertQueryEquals("a&&b", a, "a&&b");
            assertQueryEquals(".NET", a, ".NET");
        }

        [Test]
        public void testSlop()
        {
            assertQueryEquals("\"term germ\"~2", null, "\"term germ\"~2");
            assertQueryEquals("\"term germ\"~2 flork", null, "\"term germ\"~2 flork");
            assertQueryEquals("\"term\"~2", null, "term");
            assertQueryEquals("\" \"~2 germ", null, "germ");
            assertQueryEquals("\"term germ\"~2^2", null, "\"term germ\"~2^2.0");
        }

        [Test]
        public void testNumber()
        {
            // The numbers go away because SimpleAnalzyer ignores them
            assertQueryEquals("3", null, "");
            assertQueryEquals("term 1.0 1 2", null, "term");
            assertQueryEquals("term term1 term2", null, "term term term");

            Analyzer a = new MockAnalyzer(Random());
            assertQueryEquals("3", a, "3");
            assertQueryEquals("term 1.0 1 2", a, "term 1.0 1 2");
            assertQueryEquals("term term1 term2", a, "term term1 term2");
        }

        [Test]
        public void testWildcard()
        {
            assertQueryEquals("term*", null, "term*");
            assertQueryEquals("term*^2", null, "term*^2.0");
            assertQueryEquals("term~", null, "term~2");
            assertQueryEquals("term~0.7", null, "term~1");
            assertQueryEquals("term~^3", null, "term~2^3.0");
            assertQueryEquals("term^3~", null, "term~2^3.0");
            assertQueryEquals("term*germ", null, "term*germ");
            assertQueryEquals("term*germ^3", null, "term*germ^3.0");

            assertTrue(GetQuery("term*", null) is PrefixQuery);
            assertTrue(GetQuery("term*^2", null) is PrefixQuery);
            assertTrue(GetQuery("term~", null) is FuzzyQuery);
            assertTrue(GetQuery("term~0.7", null) is FuzzyQuery);
            FuzzyQuery fq = (FuzzyQuery)GetQuery("term~0.7", null);
            assertEquals(1, fq.MaxEdits);
            assertEquals(FuzzyQuery.DefaultPrefixLength, fq.PrefixLength);
            fq = (FuzzyQuery)GetQuery("term~", null);
            assertEquals(2, fq.MaxEdits);
            assertEquals(FuzzyQuery.DefaultPrefixLength, fq.PrefixLength);
            try
            {
                GetQuery("term~1.1", null); // value > 1, throws exception
                fail();
            }
            catch (ParseException pe)
            {
                // expected exception
            }
            assertTrue(GetQuery("term*germ", null) is WildcardQuery);

            /*
             * Tests to see that wild card terms are (or are not) properly lower-cased
             * with propery parser configuration
             */
            // First prefix queries:
            // by default, convert to lowercase:
            assertWildcardQueryEquals("Term*", true, "term*");
            // explicitly set lowercase:
            assertWildcardQueryEquals("term*", true, "term*");
            assertWildcardQueryEquals("Term*", true, "term*");
            assertWildcardQueryEquals("TERM*", true, "term*");
            // explicitly disable lowercase conversion:
            assertWildcardQueryEquals("term*", false, "term*");
            assertWildcardQueryEquals("Term*", false, "Term*");
            assertWildcardQueryEquals("TERM*", false, "TERM*");
            // Then 'full' wildcard queries:
            // by default, convert to lowercase:
            assertWildcardQueryEquals("Te?m", "te?m");
            // explicitly set lowercase:
            assertWildcardQueryEquals("te?m", true, "te?m");
            assertWildcardQueryEquals("Te?m", true, "te?m");
            assertWildcardQueryEquals("TE?M", true, "te?m");
            assertWildcardQueryEquals("Te?m*gerM", true, "te?m*germ");
            // explicitly disable lowercase conversion:
            assertWildcardQueryEquals("te?m", false, "te?m");
            assertWildcardQueryEquals("Te?m", false, "Te?m");
            assertWildcardQueryEquals("TE?M", false, "TE?M");
            assertWildcardQueryEquals("Te?m*gerM", false, "Te?m*gerM");
            // Fuzzy queries:
            assertWildcardQueryEquals("Term~", "term~2");
            assertWildcardQueryEquals("Term~", true, "term~2");
            assertWildcardQueryEquals("Term~", false, "Term~2");
            // Range queries:
            assertWildcardQueryEquals("[A TO C]", "[a TO c]");
            assertWildcardQueryEquals("[A TO C]", true, "[a TO c]");
            assertWildcardQueryEquals("[A TO C]", false, "[A TO C]");
        }

        [Test]
        public void testQPA()
        {
            assertQueryEquals("term term term", qpAnalyzer, "term term term");
            assertQueryEquals("term +stop term", qpAnalyzer, "term term");
            assertQueryEquals("term -stop term", qpAnalyzer, "term term");
            assertQueryEquals("drop AND stop AND roll", qpAnalyzer, "+drop +roll");
            assertQueryEquals("term phrase term", qpAnalyzer,
                "term (phrase1 phrase2) term");
            // note the parens in this next assertion differ from the original
            // QueryParser behavior
            assertQueryEquals("term AND NOT phrase term", qpAnalyzer,
                "(+term -(phrase1 phrase2)) term");
            assertQueryEquals("stop", qpAnalyzer, "");
            assertQueryEquals("stop OR stop AND stop", qpAnalyzer, "");
            assertTrue(GetQuery("term term term", qpAnalyzer) is BooleanQuery);
            assertTrue(GetQuery("term +stop", qpAnalyzer) is TermQuery);
        }

        [Test]
        public void testRange()
        {
            assertQueryEquals("[ a TO z]", null, "[a TO z]");
            assertTrue(GetQuery("[ a TO z]", null) is TermRangeQuery);
            assertQueryEquals("[ a TO z ]", null, "[a TO z]");
            assertQueryEquals("{ a TO z}", null, "{a TO z}");
            assertQueryEquals("{ a TO z }", null, "{a TO z}");
            assertQueryEquals("{ a TO z }^2.0", null, "{a TO z}^2.0");
            assertQueryEquals("[ a TO z] OR bar", null, "[a TO z] bar");
            assertQueryEquals("[ a TO z] AND bar", null, "+[a TO z] +bar");
            assertQueryEquals("( bar blar { a TO z}) ", null, "bar blar {a TO z}");
            assertQueryEquals("gack ( bar blar { a TO z}) ", null,
                "gack (bar blar {a TO z})");
        }

        private String escapeDateString(String s)
        {
            if (s.Contains(" "))
            {
                return "\"" + s + "\"";
            }
            else
            {
                return s;
            }
        }

        public String getDate(String s)
        {
            // we use the default Locale since LuceneTestCase randomizes it
            //DateFormat df = DateFormat.getDateInstance(DateFormat.SHORT, Locale.getDefault());
            //return DateTools.DateToString(df.parse(s), DateTools.Resolution.DAY);

            //DateFormat df = DateFormat.getDateInstance(DateFormat.SHORT, Locale.getDefault());
            return DateTools.DateToString(DateTime.Parse(s), DateTools.Resolution.DAY);
        }

        private String getLocalizedDate(int year, int month, int day,
            bool extendLastDate)
        {
            //// we use the default Locale/TZ since LuceneTestCase randomizes it
            //DateFormat df = DateFormat.getDateInstance(DateFormat.SHORT, Locale.getDefault());
            //Calendar calendar = new GregorianCalendar(TimeZone.getDefault(), Locale.getDefault());
            //calendar.set(year, month, day);
            //if (extendLastDate)
            //{
            //    calendar.set(Calendar.HOUR_OF_DAY, 23);
            //    calendar.set(Calendar.MINUTE, 59);
            //    calendar.set(Calendar.SECOND, 59);
            //    calendar.set(Calendar.MILLISECOND, 999);
            //}
            //return df.format(calendar.getTime());


            var calendar = CultureInfo.CurrentCulture.Calendar;
            DateTime lastDate = new DateTime(year, month, day, calendar);

            if (extendLastDate)
            {
                lastDate = calendar.AddHours(lastDate, 23);
                lastDate = calendar.AddMinutes(lastDate, 59);
                lastDate = calendar.AddSeconds(lastDate, 59);
                lastDate = calendar.AddMilliseconds(lastDate, 999);
            }

            return lastDate.ToShortDateString();
        }

        [Test]
        public void testDateRange()
        {
            String startDate = getLocalizedDate(2002, 1, 1, false);
            String endDate = getLocalizedDate(2002, 1, 4, false);
            // we use the default Locale/TZ since LuceneTestCase randomizes it
            //Calendar endDateExpected = new GregorianCalendar(TimeZone.getDefault(), Locale.getDefault());
            //endDateExpected.set(2002, 1, 4, 23, 59, 59);
            //endDateExpected.set(Calendar.MILLISECOND, 999);
            DateTime endDateExpected = new DateTime(2002, 1, 4, 23, 59, 59, 999, new GregorianCalendar());


            String defaultField = "default";
            String monthField = "month";
            String hourField = "hour";
            PrecedenceQueryParser qp = new PrecedenceQueryParser(new MockAnalyzer(Random()));

            // LUCENENET TODO: Can we eliminate this nullable??
            IDictionary<string, DateTools.Resolution?> fieldMap = new HashMap<string, DateTools.Resolution?>();
            // set a field specific date resolution
            fieldMap.Put(monthField, DateTools.Resolution.MONTH);
            qp.SetDateResolution(fieldMap);

            // set default date resolution to MILLISECOND
            qp.SetDateResolution(DateTools.Resolution.MILLISECOND);

            // set second field specific date resolution
            fieldMap.Put(hourField, DateTools.Resolution.HOUR);
            qp.SetDateResolution(fieldMap);

            // for this field no field specific date resolution has been set,
            // so verify if the default resolution is used
            assertDateRangeQueryEquals(qp, defaultField, startDate, endDate,
                endDateExpected, DateTools.Resolution.MILLISECOND);

            // verify if field specific date resolutions are used for these two fields
            assertDateRangeQueryEquals(qp, monthField, startDate, endDate,
                endDateExpected, DateTools.Resolution.MONTH);

            assertDateRangeQueryEquals(qp, hourField, startDate, endDate,
                endDateExpected, DateTools.Resolution.HOUR);
        }

        /** for testing DateTools support */
        private String getDate(String s, DateTools.Resolution resolution)
        {
            // we use the default Locale since LuceneTestCase randomizes it
            //DateFormat df = DateFormat.getDateInstance(DateFormat.SHORT, Locale.getDefault());
            //return getDate(df.parse(s), resolution);
            return getDate(DateTime.Parse(s), resolution);
        }

        /** for testing DateTools support */
        private String getDate(DateTime d, DateTools.Resolution resolution)
        {
            return DateTools.DateToString(d, resolution);
        }

        public void assertQueryEquals(PrecedenceQueryParser qp, String field, String query,
            String result)
        {
            Query q = (Query)qp.Parse(query, field);
            String s = q.ToString(field);
            if (!s.equals(result))
            {
                fail("Query /" + query + "/ yielded /" + s + "/, expecting /" + result
                    + "/");
            }
        }

        public void assertDateRangeQueryEquals(PrecedenceQueryParser qp, String field,
            String startDate, String endDate, DateTime endDateInclusive,
            DateTools.Resolution resolution)
        {
            assertQueryEquals(qp, field, field + ":[" + escapeDateString(startDate)
                + " TO " + escapeDateString(endDate) + "]", "["
                + getDate(startDate, resolution) + " TO "
                + getDate(endDateInclusive, resolution) + "]");
            assertQueryEquals(qp, field, field + ":{" + escapeDateString(startDate)
                + " TO " + escapeDateString(endDate) + "}", "{"
                + getDate(startDate, resolution) + " TO "
                + getDate(endDate, resolution) + "}");
        }

        [Test]
        public void testEscaped()
        {
            Analyzer a = new MockAnalyzer(Random(), MockTokenizer.WHITESPACE, false);

            assertQueryEquals("a\\-b:c", a, "a-b:c");
            assertQueryEquals("a\\+b:c", a, "a+b:c");
            assertQueryEquals("a\\:b:c", a, "a:b:c");
            assertQueryEquals("a\\\\b:c", a, "a\\b:c");

            assertQueryEquals("a:b\\-c", a, "a:b-c");
            assertQueryEquals("a:b\\+c", a, "a:b+c");
            assertQueryEquals("a:b\\:c", a, "a:b:c");
            assertQueryEquals("a:b\\\\c", a, "a:b\\c");

            assertQueryEquals("a:b\\-c*", a, "a:b-c*");
            assertQueryEquals("a:b\\+c*", a, "a:b+c*");
            assertQueryEquals("a:b\\:c*", a, "a:b:c*");

            assertQueryEquals("a:b\\\\c*", a, "a:b\\c*");

            assertQueryEquals("a:b\\-?c", a, "a:b-?c");
            assertQueryEquals("a:b\\+?c", a, "a:b+?c");
            assertQueryEquals("a:b\\:?c", a, "a:b:?c");

            assertQueryEquals("a:b\\\\?c", a, "a:b\\?c");

            assertQueryEquals("a:b\\-c~", a, "a:b-c~2");
            assertQueryEquals("a:b\\+c~", a, "a:b+c~2");
            assertQueryEquals("a:b\\:c~", a, "a:b:c~2");
            assertQueryEquals("a:b\\\\c~", a, "a:b\\c~2");

            assertQueryEquals("[ a\\- TO a\\+ ]", null, "[a- TO a+]");
            assertQueryEquals("[ a\\: TO a\\~ ]", null, "[a: TO a~]");
            assertQueryEquals("[ a\\\\ TO a\\* ]", null, "[a\\ TO a*]");
        }

        [Test]
        public void testTabNewlineCarriageReturn()
        {
            assertQueryEqualsDOA("+weltbank +worlbank", null, "+weltbank +worlbank");

            assertQueryEqualsDOA("+weltbank\n+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \n+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \n +worlbank", null, "+weltbank +worlbank");

            assertQueryEqualsDOA("+weltbank\r+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \r+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \r +worlbank", null, "+weltbank +worlbank");

            assertQueryEqualsDOA("+weltbank\r\n+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \r\n+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \r\n +worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \r \n +worlbank", null,
                "+weltbank +worlbank");

            assertQueryEqualsDOA("+weltbank\t+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \t+worlbank", null, "+weltbank +worlbank");
            assertQueryEqualsDOA("weltbank \t +worlbank", null, "+weltbank +worlbank");
        }

        [Test]
        public void testSimpleDAO()
        {
            assertQueryEqualsDOA("term term term", null, "+term +term +term");
            assertQueryEqualsDOA("term +term term", null, "+term +term +term");
            assertQueryEqualsDOA("term term +term", null, "+term +term +term");
            assertQueryEqualsDOA("term +term +term", null, "+term +term +term");
            assertQueryEqualsDOA("-term term term", null, "-term +term +term");
        }

        [Test]
        public void testBoost()
        {
            CharacterRunAutomaton stopSet = new CharacterRunAutomaton(BasicAutomata.MakeString("on"));
            Analyzer oneStopAnalyzer = new MockAnalyzer(Random(), MockTokenizer.SIMPLE, true, stopSet);

            PrecedenceQueryParser qp = new PrecedenceQueryParser();
            qp.Analyzer = (oneStopAnalyzer);
            Query q = (Query)qp.Parse("on^1.0", "field");
            assertNotNull(q);
            q = (Query)qp.Parse("\"hello\"^2.0", "field");
            assertNotNull(q);
            assertEquals(q.Boost, (float)2.0, (float)0.5);
            q = (Query)qp.Parse("hello^2.0", "field");
            assertNotNull(q);
            assertEquals(q.Boost, (float)2.0, (float)0.5);
            q = (Query)qp.Parse("\"on\"^1.0", "field");
            assertNotNull(q);

            q = (Query)GetParser(new MockAnalyzer(Random(), MockTokenizer.SIMPLE, true, MockTokenFilter.ENGLISH_STOPSET)).Parse("the^3",
                    "field");
            assertNotNull(q);
        }

        [Test]
        public void testException()
        {
            try
            {
                assertQueryEquals("\"some phrase", null, "abc");
                fail("ParseException expected, not thrown");
            }
            catch (QueryNodeParseException expected)
            {
            }
        }

        [Test]
        public void testBooleanQuery()
        {
            BooleanQuery.MaxClauseCount = (2);
            try
            {
                GetParser(new MockAnalyzer(Random(), MockTokenizer.WHITESPACE, false)).Parse("one two three", "field");
                fail("ParseException expected due to too many boolean clauses");
            }
            catch (QueryNodeException expected)
            {
                // too many boolean clauses, so ParseException is expected
            }
        }

        // LUCENE-792
        [Test]
        public void testNOT()
        {
            Analyzer a = new MockAnalyzer(Random(), MockTokenizer.WHITESPACE, false);
            assertQueryEquals("NOT foo AND bar", a, "-foo +bar");
        }

        /**
         * This test differs from the original QueryParser, showing how the precedence
         * issue has been corrected.
         */
        [Test]
        public void testPrecedence()
        {
            PrecedenceQueryParser parser = GetParser(new MockAnalyzer(Random(), MockTokenizer.WHITESPACE, false));
            Query query1 = (Query)parser.Parse("A AND B OR C AND D", "field");
            Query query2 = (Query)parser.Parse("(A AND B) OR (C AND D)", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A OR B C", "field");
            query2 = (Query)parser.Parse("(A B) C", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND B C", "field");
            query2 = (Query)parser.Parse("(+A +B) C", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND NOT B", "field");
            query2 = (Query)parser.Parse("+A -B", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A OR NOT B", "field");
            query2 = (Query)parser.Parse("A -B", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A OR NOT B AND C", "field");
            query2 = (Query)parser.Parse("A (-B +C)", "field");
            assertEquals(query1, query2);

            parser.SetDefaultOperator(/*StandardQueryConfigHandler.*/Operator.AND);
            query1 = (Query)parser.Parse("A AND B OR C AND D", "field");
            query2 = (Query)parser.Parse("(A AND B) OR (C AND D)", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND B C", "field");
            query2 = (Query)parser.Parse("(A B) C", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND B C", "field");
            query2 = (Query)parser.Parse("(+A +B) C", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND NOT B", "field");
            query2 = (Query)parser.Parse("+A -B", "field");
            assertEquals(query1, query2);

            query1 = (Query)parser.Parse("A AND NOT B OR C", "field");
            query2 = (Query)parser.Parse("(+A -B) OR C", "field");
            assertEquals(query1, query2);

        }


        public override void TearDown()
        {
            BooleanQuery.MaxClauseCount = (originalMaxClauses);
            base.TearDown();
        }
    }
}