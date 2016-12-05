﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AE.Net.Mail.Imap;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Shouldly;

namespace Tests
{
    [TestClass]
    public class ModifiedUtf7EncodingTest {

        #region Fields

        private ModifiedUtf7Encoding _utf7 = new ModifiedUtf7Encoding();

        #endregion Fields

        #region Methods

        [TestMethod]
        public void Decode_AsciiOnlyInput_ReturnsOriginalString() {
            string mailboxNameWithAsciiOnly = "Sent";

            string result = _utf7.Decode(mailboxNameWithAsciiOnly);

            result.ShouldBe(mailboxNameWithAsciiOnly);
        }

        [TestMethod]
        public void Decode_InputNull_ReturnsNull()
        {
            _utf7.Decode(null).ShouldBe(null);
        }

        [TestMethod]
        public void Decode_InputWithConventionalAmpersand_ReturnsStringWithDecodedAmpersand()
        {
            string mailboxWithAmpersand = "Test &- Test";

            string result = _utf7.Decode(mailboxWithAmpersand);

            result.ShouldBe("Test & Test");
        }

        [TestMethod]
        public void Decode_InputWithEncodedCyrillicCharacters_ReturnsDecodedCyrillicCharacters()
        {
            string mailboxNameWithCyrillicCharacters = "&BB4EQgQ,BEAEMAQyBDsENQQ9BD0ESwQ1-";

            string result = _utf7.Decode(mailboxNameWithCyrillicCharacters);

            result.ShouldBe("Отправленные");
        }

        [TestMethod]
        public void Decode_InputWithEncodedUmlaut_ReturnsStringWithDecodedUmlaut() {
            string mailboxNameWithUmlaut = "Entw&APw-rfe";

            string result = _utf7.Decode(mailboxNameWithUmlaut);

            result.ShouldBe("Entwürfe");
        }
        [TestMethod]
        public void Encode_AsciiOnlyInput_ReturnsOriginalString() {
            string mailboxNameWithAsciiOnly = "Sent";

            string result = _utf7.Encode(mailboxNameWithAsciiOnly);

            result.ShouldBe(mailboxNameWithAsciiOnly);
        }

        [TestMethod]
        public void Encode_InputNull_ReturnsNull()
        {
            _utf7.Encode(null).ShouldBe(null);
        }

        [TestMethod]
        public void Encode_InputWithConventionalAmpersand_ReturnsStringWithAmpersandMinus()
        {
            string mailboxWithAmpersand = "Test & Test";

            string result = _utf7.Encode(mailboxWithAmpersand);

            result.ShouldBe("Test &- Test");
        }

        [TestMethod]
        public void Encode_InputWithCyrillicCharacters_ReturnsStringWithEncodedCyrillicCharacters()
        {
            string mailboxNameWithCyrillicCharacters = "Отправленные";

            string result = _utf7.Encode(mailboxNameWithCyrillicCharacters);

            result.ShouldBe("&BB4EQgQ,BEAEMAQyBDsENQQ9BD0ESwQ1-");
        }

        [TestMethod]
        public void Encode_InputWithUmlaut_ReturnsStringWithEncodedUmlaut() {
            string mailboxNameWithUmlaut = "Entwürfe";

            string result = _utf7.Encode(mailboxNameWithUmlaut);

            result.ShouldBe("Entw&APw-rfe");
        }

        #endregion Methods

    }
}
