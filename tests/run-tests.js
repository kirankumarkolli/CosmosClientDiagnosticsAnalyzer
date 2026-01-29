/**
 * Validation Test Suite for CosmosClientDiagnostics Analyzer
 * 
 * Run with: node tests/run-tests.js
 * 
 * Requires puppeteer: npm install puppeteer
 */
const puppeteer = require('puppeteer');
const path = require('path');
const fs = require('fs');

const TESTS = [];
let browser;
let page;

// Load test fixtures
const FIXTURES_DIR = path.join(__dirname, 'fixtures');

function loadFixture(filename) {
    const filepath = path.join(FIXTURES_DIR, filename);
    if (fs.existsSync(filepath)) {
        return fs.readFileSync(filepath, 'utf-8');
    }
    return null;
}

// Test registration helper
function test(name, fn) {
    TESTS.push({ name, fn });
}

// =============================================================================
// TEST: GroupBy TransportException Key Truncation
// Issue: Exception messages with different timestamps were creating separate groups
// Fix: Truncate key at "(Time:" to group similar exceptions together
// =============================================================================
test('GroupBy TransportException: truncates key at (Time: to group similar exceptions', async () => {
    const result = await page.evaluate(() => {
        const analyzer = new Analyzer();
        
        // Mock network interactions with transport exceptions containing timestamps
        // Simulates real Cosmos DB TransportException messages with (Time:...) suffix
        const mockInteractions = [
            { transportException: 'StatusCode: 410, ReasonPhrase: \'Gone\', Server requested retries due to gone. (Time: 2026-01-29T10:00:00Z)', durationInMs: 1000 },
            { transportException: 'StatusCode: 410, ReasonPhrase: \'Gone\', Server requested retries due to gone. (Time: 2026-01-29T10:01:00Z)', durationInMs: 1500 },
            { transportException: 'StatusCode: 410, ReasonPhrase: \'Gone\', Server requested retries due to gone. (Time: 2026-01-29T10:02:00Z)', durationInMs: 2000 },
            { transportException: 'Channel acquisition failed due to timeout. (Time: 2026-01-29T10:00:00Z)', durationInMs: 500 },
            { transportException: 'Channel acquisition failed due to timeout. (Time: 2026-01-29T10:05:00Z)', durationInMs: 600 },
            { transportException: 'Connection refused by remote host', durationInMs: 300 }
        ];
        
        // Use the groupBy function with the same key function as in the analyzer
        const groups = analyzer.groupBy(
            mockInteractions.filter(n => n.transportException),
            n => {
                const exception = n.transportException || 'None';
                const timeIndex = exception.indexOf('(Time:');
                return timeIndex !== -1 ? exception.substring(0, timeIndex).trim() : exception;
            }
        );
        
        return {
            groupCount: groups.length,
            groups: groups.map(g => ({ key: g.key, count: g.count }))
        };
    });
    
    // Assertions
    if (result.groupCount !== 3) {
        throw new Error(`Expected 3 groups, got ${result.groupCount}. Groups: ${JSON.stringify(result.groups)}`);
    }
    
    const goneGroup = result.groups.find(g => g.key.includes('410'));
    if (!goneGroup || goneGroup.count !== 3) {
        throw new Error('Expected 410 Gone group with 3 entries');
    }
    
    const channelGroup = result.groups.find(g => g.key.includes('Channel acquisition'));
    if (!channelGroup || channelGroup.count !== 2) {
        throw new Error('Expected "Channel acquisition" group with 2 entries');
    }
    
    const noTimestampGroup = result.groups.find(g => g.key === 'Connection refused by remote host');
    if (!noTimestampGroup || noTimestampGroup.count !== 1) {
        throw new Error('Expected "Connection refused by remote host" group with 1 entry (unchanged key)');
    }
});

// =============================================================================
// TEST: GroupBy LastTransportEvent in Multi-Entry Mode
// Issue: transportEventGroups was empty when file uploaded or multiple JSON entries input
// Fix: Use highLatency filtered entries instead of all diagnostics for targetDiags
// =============================================================================
test('GroupBy LastTransportEvent: shows in multi-entry mode', async () => {
    const result = await page.evaluate(() => {
        const analyzer = new Analyzer();
        
        // Mock diagnostics simulating multi-entry file upload
        const mockDiagnostics = [
            {
                name: 'ReadItem',
                duration: 800,  // Above 600ms threshold
                startTime: '2026-01-29T10:00:00Z',
                data: {
                    clientSideRequestStats: {
                        storeResponseStatistics: [{
                            resourceType: 'Document',
                            operationType: 'Read',
                            durationInMs: 750,
                            storeResult: {
                                statusCode: 200,
                                subStatusCode: 0,
                                storePhysicalAddress: 'https://cosmos-node1.documents.azure.com:443/',
                                transportRequestTimeline: {
                                    requestTimeline: [
                                        { event: 'Created', durationInMs: 1 },
                                        { event: 'ChannelAcquisitionStarted', durationInMs: 10 },
                                        { event: 'Pipelined', durationInMs: 5 },
                                        { event: 'Transit Time', durationInMs: 600 },
                                        { event: 'Received', durationInMs: 50 },
                                        { event: 'Completed', durationInMs: 2 }
                                    ]
                                }
                            }
                        }]
                    }
                },
                children: []
            },
            {
                name: 'ReadItem',
                duration: 900,  // Above 600ms threshold
                startTime: '2026-01-29T10:01:00Z',
                data: {
                    clientSideRequestStats: {
                        storeResponseStatistics: [{
                            resourceType: 'Document',
                            operationType: 'Read',
                            durationInMs: 850,
                            storeResult: {
                                statusCode: 200,
                                subStatusCode: 0,
                                storePhysicalAddress: 'https://cosmos-node2.documents.azure.com:443/',
                                transportRequestTimeline: {
                                    requestTimeline: [
                                        { event: 'Created', durationInMs: 1 },
                                        { event: 'ChannelAcquisitionStarted', durationInMs: 200 },
                                        { event: 'Pipelined', durationInMs: 5 },
                                        { event: 'Transit Time', durationInMs: 500 },
                                        { event: 'Received', durationInMs: 50 },
                                        { event: 'Completed', durationInMs: 2 }
                                    ]
                                }
                            }
                        }]
                    }
                },
                children: []
            },
            {
                name: 'ReadItem',
                duration: 400,  // BELOW 600ms threshold - should be excluded
                startTime: '2026-01-29T10:02:00Z',
                data: {
                    clientSideRequestStats: {
                        storeResponseStatistics: [{
                            resourceType: 'Document',
                            operationType: 'Read',
                            durationInMs: 350,
                            storeResult: {
                                statusCode: 200,
                                subStatusCode: 0,
                                storePhysicalAddress: 'https://cosmos-node3.documents.azure.com:443/',
                                transportRequestTimeline: {
                                    requestTimeline: [
                                        { event: 'Created', durationInMs: 1 },
                                        { event: 'Completed', durationInMs: 1 }
                                    ]
                                }
                            }
                        }]
                    }
                },
                children: []
            }
        ];
        
        // Analyze with multi-entry mode (skipLatencyFilter = false, threshold = 600)
        const analysisResult = analyzer.analyze(mockDiagnostics, 600, null, false);
        
        return {
            totalEntries: analysisResult.totalEntries,
            highLatencyEntries: analysisResult.highLatencyEntries,
            networkInteractionsCount: analysisResult.networkInteractions.length,
            transportEventGroupsCount: analysisResult.transportEventGroups.length,
            transportEventGroups: analysisResult.transportEventGroups.map(g => ({
                status: g.status,
                count: g.count
            }))
        };
    });
    
    // Assertions
    if (result.totalEntries !== 3) {
        throw new Error(`Expected 3 total entries, got ${result.totalEntries}`);
    }
    
    if (result.highLatencyEntries !== 2) {
        throw new Error(`Expected 2 high latency entries (>600ms), got ${result.highLatencyEntries}`);
    }
    
    if (result.networkInteractionsCount !== 2) {
        throw new Error(`Expected 2 network interactions (from high latency only), got ${result.networkInteractionsCount}`);
    }
    
    if (result.transportEventGroupsCount === 0) {
        throw new Error('transportEventGroups is empty - GroupBy LastTransportEvent not showing!');
    }
});

// =============================================================================
// TEST: GroupBy LastTransportEvent excludes below-threshold entries
// Ensures that network interactions from entries below threshold are not included
// =============================================================================
test('GroupBy LastTransportEvent: excludes network interactions from below-threshold entries', async () => {
    const result = await page.evaluate(() => {
        const analyzer = new Analyzer();
        
        // Create entries where only some are above threshold
        const mockDiagnostics = [
            {
                name: 'Query',
                duration: 1000,  // Above threshold
                data: {
                    clientSideRequestStats: {
                        storeResponseStatistics: [{
                            resourceType: 'Document',
                            durationInMs: 900,
                            storeResult: {
                                statusCode: 200,
                                storePhysicalAddress: 'https://node1.cosmos.azure.com/',
                                transportRequestTimeline: {
                                    requestTimeline: [{ event: 'Completed', durationInMs: 1 }]
                                }
                            }
                        }]
                    }
                },
                children: []
            },
            {
                name: 'Query',
                duration: 200,  // Below threshold
                data: {
                    clientSideRequestStats: {
                        storeResponseStatistics: [{
                            resourceType: 'Document',
                            durationInMs: 150,
                            storeResult: {
                                statusCode: 200,
                                storePhysicalAddress: 'https://node2.cosmos.azure.com/',
                                transportRequestTimeline: {
                                    requestTimeline: [{ event: 'Completed', durationInMs: 1 }]
                                }
                            }
                        }]
                    }
                },
                children: []
            }
        ];
        
        const analysisResult = analyzer.analyze(mockDiagnostics, 600, null, false);
        
        return {
            highLatencyEntries: analysisResult.highLatencyEntries,
            networkInteractionsCount: analysisResult.networkInteractions.length
        };
    });
    
    if (result.highLatencyEntries !== 1) {
        throw new Error(`Expected 1 high latency entry, got ${result.highLatencyEntries}`);
    }
    
    if (result.networkInteractionsCount !== 1) {
        throw new Error(`Expected 1 network interaction (only from high latency entry), got ${result.networkInteractionsCount}`);
    }
});

// =============================================================================
// TEST: GroupBy LastTransportEvent with real sample data
// Uses actual Cosmos DB diagnostics from fixtures/sample-diagnostics.jsonl
// =============================================================================
test('GroupBy LastTransportEvent: works with real sample diagnostics data', async () => {
    const sampleData = loadFixture('sample-diagnostics.jsonl');
    if (!sampleData) {
        throw new Error('Fixture file sample-diagnostics.jsonl not found');
    }
    
    const result = await page.evaluate((content) => {
        const parser = new JsonParser();
        const analyzer = new Analyzer();
        
        // Parse the sample JSONL file
        const diagnostics = parser.parseLines(content);
        if (!diagnostics || diagnostics.length === 0) {
            return { error: 'Failed to parse sample diagnostics' };
        }
        
        // Analyze with multi-entry mode (threshold = 600ms)
        const analysisResult = analyzer.analyze(diagnostics, 600, null, false);
        
        return {
            totalEntries: analysisResult.totalEntries,
            highLatencyEntries: analysisResult.highLatencyEntries,
            operationBucketsCount: analysisResult.operationBuckets.length,
            networkInteractionsCount: analysisResult.networkInteractions.length,
            transportEventGroupsCount: analysisResult.transportEventGroups.length,
            statusCodeGroupsCount: analysisResult.statusCodeGroups.length
        };
    }, sampleData);
    
    if (result.error) {
        throw new Error(result.error);
    }
    
    // Should have parsed entries
    if (result.totalEntries === 0) {
        throw new Error('No entries parsed from sample data');
    }
    
    // If there are high latency entries, transportEventGroups should not be empty
    if (result.highLatencyEntries > 0 && result.transportEventGroupsCount === 0) {
        throw new Error(`High latency entries exist (${result.highLatencyEntries}) but transportEventGroups is empty`);
    }
});

// =============================================================================
// Test Runner
// =============================================================================
async function runTests() {
    console.log('CosmosClientDiagnostics Analyzer - Validation Tests\n');
    console.log('='.repeat(60) + '\n');
    
    browser = await puppeteer.launch({ headless: true });
    page = await browser.newPage();
    
    // Load the analyzer page
    const indexPath = path.join(__dirname, '..', 'docs', 'index.html');
    await page.goto(`file://${indexPath}`);
    
    // Wait for Analyzer to be available
    await page.waitForFunction(() => typeof window.Analyzer !== 'undefined');
    
    let passed = 0;
    let failed = 0;
    const failures = [];
    
    for (const { name, fn } of TESTS) {
        try {
            await fn();
            console.log(`✅ PASS: ${name}`);
            passed++;
        } catch (err) {
            console.log(`❌ FAIL: ${name}`);
            console.log(`   Error: ${err.message}\n`);
            failed++;
            failures.push({ name, error: err.message });
        }
    }
    
    console.log('\n' + '='.repeat(60));
    console.log(`\nResults: ${passed} passed, ${failed} failed, ${TESTS.length} total`);
    
    if (failures.length > 0) {
        console.log('\nFailed tests:');
        failures.forEach(f => console.log(`  - ${f.name}: ${f.error}`));
    }
    
    await browser.close();
    process.exit(failed > 0 ? 1 : 0);
}

runTests().catch(err => {
    console.error('Test runner error:', err);
    process.exit(1);
});
